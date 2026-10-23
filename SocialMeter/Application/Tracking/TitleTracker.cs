using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Tracking;

/// <summary>
/// Decides which titles SocialMeter measures. The set is rebuilt from TMDB's current lists plus the month's
/// seasonal theme, persisted in TrackedTitles, and mirrored in memory for cheap lookups on every request.
/// </summary>
public sealed class TitleTracker : ITitleTracker
{
    private const string SetKey = "tracked:set";
    private const string RefreshedKey = "tracked:refreshedAt";
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly SocialMeterDbContext _db;
    private readonly ITitleService _titles;
    private readonly IMemoryCache _cache;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<TitleTracker> _logger;

    public TitleTracker(SocialMeterDbContext db, ITitleService titles, IMemoryCache cache, IOptions<SocialMeterOptions> options, ILogger<TitleTracker> logger)
    {
        _db = db;
        _titles = titles;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetReasonAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default)
    {
        var set = await GetSetAsync(ct);
        return set.GetValueOrDefault((mediaType, tmdbId));
    }

    public async Task AnnotateAsync(IEnumerable<Title> titles, CancellationToken ct = default)
    {
        var set = await GetSetAsync(ct);
        foreach (var t in titles)
        {
            t.TrackedReason = set.GetValueOrDefault((t.MediaType, t.TmdbId));
            t.Tracked = t.TrackedReason is not null;
        }
    }

    public async Task<(string Label, string Subtitle, IReadOnlyList<Title> Items)> GetSeasonalRowAsync(CancellationToken ct = default)
    {
        var theme = SeasonalTheme.For(DateTime.UtcNow);
        var movies = await _titles.DiscoverByGenreAsync(MediaType.Movie, theme.MovieGenreId, 1, ct);
        var shows = theme.TvKeywordId is { } keyword
            ? await _titles.DiscoverTvByKeywordAsync(keyword, 1, ct)
            : await _titles.DiscoverByGenreAsync(MediaType.Tv, theme.TvGenreId, 1, ct);

        // Interleave so the row isn't all movies then all shows.
        var items = movies.Items.Take(12).Zip(shows.Items.Take(8).Concat(Enumerable.Repeat<Title?>(null, 12)), (m, s) => new[] { m, s })
            .SelectMany(pair => pair)
            .OfType<Title>()
            .ToList();

        return (theme.Label, theme.Subtitle, items);
    }

    public async Task<IReadOnlyList<SeasonalRow>> GetSeasonalTopRowsAsync(string region, CancellationToken ct = default)
    {
        var theme = SeasonalTheme.For(DateTime.UtcNow);
        if (theme.Key != "halloween")
            return Array.Empty<SeasonalRow>();

        // Horror in October gets the top of the page: what is hot right now, and what is actually in theaters.
        // Each list fails independently so one bad TMDB call never blanks the other.
        var rows = new List<SeasonalRow>();
        try
        {
            // TMDB supplies the candidates (two pages, so the row stays full after silent titles drop out); what people
            // actually post decides the order. TMDB popularity alone put a lookalike release with no posts in this row.
            var pages = await Task.WhenAll(
                _titles.DiscoverRecentMoviesByGenreAsync(theme.MovieGenreId, days: 365, page: 1, ct),
                _titles.DiscoverRecentMoviesByGenreAsync(theme.MovieGenreId, days: 365, page: 2, ct));
            var candidates = pages.SelectMany(p => p.Items).ToList();
            var ranked = ConversationRanking.Rank(candidates, await ConversationSignalsAsync(MediaType.Movie, candidates, ct), 20);
            if (ranked.Count > 0)
                rows.Add(new SeasonalRow("horror_trending", "Horror trending now", "The horror people are actually posting about this week", ranked));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Horror trending row failed");
        }

        try
        {
            var theaters = await _titles.DiscoverInTheatersByGenreAsync(theme.MovieGenreId, region, days: 45, page: 1, ct);
            if (theaters.Items.Count > 0)
                rows.Add(new SeasonalRow("horror_theaters", "New horror in theaters", "Opened in the last six weeks", theaters.Items.Take(20).ToList()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Horror in theaters row failed");
        }

        return rows;
    }

    /// <summary>Counted posts from the last 7 days and overall, per candidate. Titles with no snapshot were never measured.</summary>
    private async Task<IReadOnlyDictionary<int, ConversationSignal>> ConversationSignalsAsync(MediaType mediaType, IReadOnlyList<Title> titles, CancellationToken ct)
    {
        var ids = titles.Select(t => t.TmdbId).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, ConversationSignal>();

        var since = DateTime.UtcNow.AddDays(-7);
        var recent = await _db.SocialPosts
            .Where(p => p.MediaType == mediaType && ids.Contains(p.TmdbId) && !p.Excluded && p.CreatedAt >= since)
            .GroupBy(p => p.TmdbId)
            .Select(g => new { TmdbId = g.Key, Posts = g.Count() })
            .ToDictionaryAsync(x => x.TmdbId, x => x.Posts, ct);
        var totals = await _db.TitleSnapshots
            .Where(s => s.MediaType == mediaType && ids.Contains(s.TmdbId))
            .ToDictionaryAsync(s => s.TmdbId, s => s.TotalPosts, ct);

        return ids.ToDictionary(id => id, id => new ConversationSignal(
            recent.GetValueOrDefault(id), totals.TryGetValue(id, out var total) ? total : null));
    }

    public async Task<TrackedRefreshResult> RefreshAsync(CancellationToken ct = default)
    {
        await RefreshLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;

            // A rebuild is a few dozen TMDB and Steam list calls; repeating it within minutes only invites Steam's IP throttle.
            var last = _cache.Get<DateTime?>(RefreshedKey);
            if (last is { } recent && now - recent < TimeSpan.FromMinutes(Math.Max(0, _options.MinTrackedRefreshMinutes)))
            {
                return new TrackedRefreshResult
                {
                    RefreshedAt = recent,
                    Tracked = await _db.TrackedTitles.CountAsync(ct),
                    Theme = SeasonalTheme.For(now).Label
                };
            }
            var theme = SeasonalTheme.For(now);
            var seen = new Dictionary<(MediaType, int), string>();

            // Order matters: the first reason wins, and the more specific ones read better than "Popular".
            await CollectAsync(seen, "In theaters", p => _titles.GetNowPlayingAsync("US", p, ct), ct);
            await CollectAsync(seen, "On the air", p => _titles.GetOnTheAirAsync(p, ct), ct);
            await CollectAsync(seen, "Coming soon", p => _titles.GetUpcomingAsync("US", p, ct), ct);
            await CollectAsync(seen, "Trending", p => _titles.GetTrendingAsync(null, p, ct), ct);
            await CollectAsync(seen, theme.Label, p => _titles.DiscoverByGenreAsync(MediaType.Movie, theme.MovieGenreId, p, ct), ct);
            await CollectAsync(seen, theme.Label, p => _titles.DiscoverByGenreAsync(MediaType.Tv, theme.TvGenreId, p, ct), ct);
            if (theme.Key == "halloween")
            {
                // The two horror rows at the top of the October home page should carry readings, not padlocks.
                await CollectAsync(seen, "Horror season", p => _titles.DiscoverRecentMoviesByGenreAsync(theme.MovieGenreId, 365, p, ct), ct);
                await CollectAsync(seen, "Horror season", p => _titles.DiscoverInTheatersByGenreAsync(theme.MovieGenreId, "US", 45, p, ct), ct);
            }
            await CollectAsync(seen, "Popular", p => _titles.GetPopularAsync(MediaType.Movie, p, ct), ct);
            await CollectAsync(seen, "Popular", p => _titles.GetPopularAsync(MediaType.Tv, p, ct), ct);

            // Games: Steam's own "what's hot" lists. Nothing off these lists is tracked.
            await CollectAsync(seen, "Top seller", p => _titles.GetTrendingAsync(MediaType.Game, p, ct), ct);
            await CollectAsync(seen, "Most played", p => _titles.GetPopularAsync(MediaType.Game, p, ct), ct);
            await CollectAsync(seen, "New on Steam", p => _titles.GetNewGamesAsync(p, ct), ct);
            await CollectAsync(seen, "Coming soon", p => _titles.GetComingSoonGamesAsync(p, ct), ct);

            // Titles that earned an electric reading keep their place on merit.
            var electric = await _db.TitleSnapshots
                .Where(s => s.Score >= _options.ElectricMinScore && s.TotalPosts >= _options.ElectricMinPosts)
                .Select(s => new { s.MediaType, s.TmdbId })
                .ToListAsync(ct);
            foreach (var e in electric)
                seen.TryAdd((e.MediaType, e.TmdbId), "Electric");

            var existing = await _db.TrackedTitles.ToListAsync(ct);
            var byKey = existing.ToDictionary(t => (t.MediaType, t.TmdbId));
            int added = 0, dropped = 0;

            foreach (var ((mediaType, tmdbId), reason) in seen)
            {
                if (byKey.TryGetValue((mediaType, tmdbId), out var row))
                {
                    row.Reason = reason;
                    row.LastSeenAt = now;
                }
                else
                {
                    _db.TrackedTitles.Add(new TrackedTitle { MediaType = mediaType, TmdbId = tmdbId, Reason = reason, Since = now, LastSeenAt = now });
                    added++;
                }
            }

            var graceCutoff = now.AddHours(-_options.TrackedGraceHours);
            foreach (var row in existing.Where(r => !seen.ContainsKey((r.MediaType, r.TmdbId)) && r.LastSeenAt < graceCutoff))
            {
                _db.TrackedTitles.Remove(row);
                dropped++;
            }

            await _db.SaveChangesAsync(ct);

            var set = await LoadSetFromDbAsync(ct);
            _cache.Set(SetKey, set, TimeSpan.FromHours(_options.TrackedRefreshHours * 2));
            _cache.Set(RefreshedKey, now, TimeSpan.FromHours(_options.TrackedRefreshHours * 2));

            var result = new TrackedRefreshResult { RefreshedAt = now, Tracked = set.Count, Added = added, Dropped = dropped, Theme = theme.Label };
            _logger.LogInformation("Tracked set refreshed: {Tracked} titles ({Added} added, {Dropped} dropped), theme {Theme}", set.Count, added, dropped, theme.Label);
            return result;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task CollectAsync(Dictionary<(MediaType, int), string> seen, string reason, Func<int, Task<PagedResult<Title>>> fetch, CancellationToken ct)
    {
        for (var page = 1; page <= Math.Max(1, _options.TrackedListPages); page++)
        {
            PagedResult<Title> result;
            try
            {
                result = await fetch(page);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tracked-set source '{Reason}' page {Page} failed", reason, page);
                return;
            }

            foreach (var t in result.Items)
                seen.TryAdd((t.MediaType, t.TmdbId), reason);

            if (page >= result.TotalPages) break;
        }
    }

    private async Task<Dictionary<(MediaType, int), string>> GetSetAsync(CancellationToken ct)
    {
        var refreshedAt = _cache.Get<DateTime?>(RefreshedKey);
        if (refreshedAt is null || DateTime.UtcNow - refreshedAt > TimeSpan.FromHours(_options.TrackedRefreshHours))
        {
            // First request after start-up, or the worker is behind: rebuild now so nothing is wrongly locked.
            var set = await LoadSetFromDbAsync(ct);
            var newest = await _db.TrackedTitles.MaxAsync(t => (DateTime?)t.LastSeenAt, ct);
            if (set.Count == 0 || newest is null || DateTime.UtcNow - newest > TimeSpan.FromHours(_options.TrackedRefreshHours))
                await RefreshAsync(ct);
            else
            {
                _cache.Set(SetKey, set, TimeSpan.FromHours(_options.TrackedRefreshHours * 2));
                _cache.Set(RefreshedKey, newest.Value, TimeSpan.FromHours(_options.TrackedRefreshHours * 2));
            }
        }

        return _cache.Get<Dictionary<(MediaType, int), string>>(SetKey) ?? await LoadSetFromDbAsync(ct);
    }

    private async Task<Dictionary<(MediaType, int), string>> LoadSetFromDbAsync(CancellationToken ct)
    {
        var rows = await _db.TrackedTitles.AsNoTracking().ToListAsync(ct);
        return rows.ToDictionary(r => (r.MediaType, r.TmdbId), r => r.Reason);
    }
}
