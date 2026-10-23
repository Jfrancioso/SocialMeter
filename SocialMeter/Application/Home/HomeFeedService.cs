using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Home;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Application.Home;

/// <summary>
/// Builds the home page (hero + rows) and the search box's suggestions.
///
/// The rows come in two kinds, kept apart on purpose:
///  1. Rows that only call outside services (TMDB, Steam) are started at once and run side by side.
///  2. Steps that read our own database run one after another. A request's DbContext runs one query at a time; running
///     two side by side is what took the home page down on 2026-10-23. They still overlap with the rows from kind 1.
/// A new row goes in exactly one of the two places below. Nothing else decides where it runs.
/// </summary>
public sealed class HomeFeedService
{
    private static readonly TimeSpan HomeCacheFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TalkedAboutCacheFor = TimeSpan.FromMinutes(15);

    private readonly ITitleService _titles;
    private readonly ITitleTracker _tracker;
    private readonly IPostQueries _queries;
    private readonly SocialMeterOptions _options;
    private readonly HomeOptions _home;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HomeFeedService> _logger;

    public HomeFeedService(ITitleService titles, ITitleTracker tracker, IPostQueries queries, IOptions<SocialMeterOptions> options,
        IOptions<HomeOptions> home, IMemoryCache cache, ILogger<HomeFeedService> logger)
    {
        _titles = titles;
        _tracker = tracker;
        _queries = queries;
        _options = options.Value;
        _home = home.Value;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>The home page for a region (a two-letter code the caller has already checked), cached for five minutes.</summary>
    public async Task<HomeResponseDto> GetHomeAsync(string region, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(HomeKey(region), out HomeResponseDto? cached) && cached is not null)
            return cached;
        return await BuildHomeAsync(region, ct);
    }

    private static string HomeKey(string region) => $"home:{region}";

    /// <summary>
    /// Builds the home page now and caches it, whatever is cached already. HomeWarmupWorker calls this before the cached
    /// copy expires, so no visitor waits the ~3 seconds a build takes.
    /// </summary>
    public async Task<HomeResponseDto> BuildHomeAsync(string region, CancellationToken ct = default)
    {
        var key = HomeKey(region);

        var wanted = _home.Effective;
        bool Wants(params string[] keys) => keys.Any(wanted.Contains);

        // ---- 1. Outside services only: started now, awaited together at the end. ----
        // Trending is always fetched: the hero falls back to it.
        var trendingTask = _titles.GetTrendingAsync(null, 1, ct);
        var running = new List<Task<IReadOnlyList<HomeRowDto>>>();
        void Start(string rowKey, Func<Task<IReadOnlyList<HomeRowDto>>> build)
        {
            if (wanted.Contains(rowKey)) running.Add(build());
        }

        Start(HomeRowKeys.Trending, async () => Row(HomeRowKeys.Trending, "Trending now", "What the internet is talking about today", (await trendingTask).Items));
        Start(HomeRowKeys.NowPlaying, async () => Row(HomeRowKeys.NowPlaying, "In theaters", "Playing near you", (await _titles.GetNowPlayingAsync(region, 1, ct)).Items));
        Start(HomeRowKeys.Seasonal, async () =>
        {
            var (label, subtitle, items) = await _tracker.GetSeasonalRowAsync(ct);
            return Row(HomeRowKeys.Seasonal, label, subtitle, items);
        });
        Start(HomeRowKeys.OnTheAir, async () => Row(HomeRowKeys.OnTheAir, "On TV this week", "Shows with new episodes airing", (await _titles.GetOnTheAirAsync(1, ct)).Items));
        Start(HomeRowKeys.TopGames, async () => Row(HomeRowKeys.TopGames, "Hot on Steam", "This week's top sellers", await SteamAsync(_titles.GetTrendingAsync(MediaType.Game, 1, ct))));
        Start(HomeRowKeys.PopularMovies, async () => Row(HomeRowKeys.PopularMovies, "Popular movies", null, (await _titles.GetPopularAsync(MediaType.Movie, 1, ct)).Items));
        Start(HomeRowKeys.PopularTv, async () => Row(HomeRowKeys.PopularTv, "Popular shows", null, (await _titles.GetPopularAsync(MediaType.Tv, 1, ct)).Items));
        Start(HomeRowKeys.MostPlayed, async () => Row(HomeRowKeys.MostPlayed, "Most played right now", "Steam's live player counts", await SteamAsync(_titles.GetPopularAsync(MediaType.Game, 1, ct))));

        // ---- 2. Our database, one step at a time. Each step that fails drops its rows; the rest of the page still renders. ----
        if (Wants(HomeRowKeys.Gaps))
        {
            var gaps = await ReadAsync("Crowd vs critics", async () => HomeSignals.BiggestGaps(await _queries.GetCrowdCriticsAsync(ct),
                _home.GapMinPosts, _home.GapMinConfidence, _home.GapMinPoints, _home.SignalRowSize));
            // Their details come from TMDB, so that part joins the rows already running.
            if (gaps is { Count: > 0 })
                running.Add(SignalRowAsync(HomeRowKeys.Gaps, "Crowd vs critics", "Where the people and the critics disagree most", gaps.Select(g => (g.MediaType, g.TmdbId)), ct));
        }

        // Both momentum rows come from one trend run; each stays hidden until the trend test has enough days to say anything.
        if (Wants(HomeRowKeys.HeatingUp, HomeRowKeys.CoolingOff))
        {
            var movers = await ReadAsync("Movers", () => MoversAsync(ct));
            if (movers is not null)
            {
                running.Add(SignalRowAsync(HomeRowKeys.HeatingUp, "Heating up", "Crowd scores climbing day after day", movers.Rising.Select(m => (m.MediaType, m.TmdbId)), ct));
                running.Add(SignalRowAsync(HomeRowKeys.CoolingOff, "Cooling off", "Crowd scores slipping day after day", movers.Falling.Select(m => (m.MediaType, m.TmdbId)), ct));
            }
        }

        // October's horror pair: TMDB supplies the candidates and our post counts rank them, so it is a database step.
        var horror = Wants(HomeRowKeys.HorrorTrending, HomeRowKeys.HorrorTheaters)
            ? (await ReadAsync("Horror rows", () => _tracker.GetSeasonalTopRowsAsync(region, ct)) ?? Array.Empty<SeasonalRow>())
                .Select(r => new HomeRowDto { Key = r.Key, Title = r.Title, Subtitle = r.Subtitle, Items = r.Items })
            : Enumerable.Empty<HomeRowDto>();

        // ---- Put the page together. From here on everything runs one step at a time again. ----
        var built = (await Task.WhenAll(running)).SelectMany(rows => rows).Concat(horror)
            .GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        var rows = HomeLayout.Dedupe(wanted.Where(built.ContainsKey).Select(k => built[k]));

        var trending = await trendingTask;
        var (featured, featuredLabel, featuredPosts) = await PickFeaturedAsync(trending.Items, ct);
        await _tracker.AnnotateAsync(rows.SelectMany(r => r.Items).Concat(featured is null ? [] : [featured]), ct);

        var response = new HomeResponseDto
        {
            Featured = featured,
            FeaturedLabel = featuredLabel,
            FeaturedPosts = featuredPosts,
            FeaturedWindowDays = _options.FeaturedWindowDays,
            Rows = rows
        };
        _cache.Set(key, response, HomeCacheFor);
        return response;
    }

    /// <summary>
    /// What the search box offers before anything is typed: the titles people are posting about most this week (from our
    /// own counts, games included) and TMDB's trending list. Built from data already on hand; nothing about the visitor
    /// or what they search for is recorded.
    /// </summary>
    public async Task<SearchSuggestionsDto> GetSuggestionsAsync(CancellationToken ct = default) =>
        _cache.TryGetValue(SuggestionsKey, out SearchSuggestionsDto? cached) && cached is not null ? cached : await BuildSuggestionsAsync(ct);

    private const string SuggestionsKey = "home:suggestions";

    /// <summary>Builds the search suggestions now and caches them, whatever is cached already (see BuildHomeAsync).</summary>
    public async Task<SearchSuggestionsDto> BuildSuggestionsAsync(CancellationToken ct = default)
    {
        var talked = new List<TalkedAboutDto>();
        try
        {
            var picks = SearchSuggestions.Pick(await MostTalkedAboutAsync(ct));
            var titles = await Task.WhenAll(picks.Select(p => DetailsOrNullAsync(p.MediaType, p.TmdbId, ct)));
            talked.AddRange(picks.Zip(titles)
                .Where(pair => pair.Second is not null)
                .Select(pair => new TalkedAboutDto { Title = pair.Second!, Posts = pair.First.Posts }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Search suggestions: most-talked-about lookup failed; offering trending only");
        }

        var trending = await _titles.GetTrendingAsync(null, 1, ct);
        var seen = talked.Select(t => (t.Title.MediaType, t.Title.TmdbId)).ToHashSet();
        var suggestions = new SearchSuggestionsDto
        {
            TalkedAbout = talked,
            Trending = trending.Items.Where(t => !seen.Contains((t.MediaType, t.TmdbId))).Take(5).ToList(),
            WindowDays = WindowDays
        };
        _cache.Set(SuggestionsKey, suggestions, TalkedAboutCacheFor);
        return suggestions;
    }

    private int WindowDays => Math.Clamp(_options.FeaturedWindowDays, 1, 14);

    /// <summary>
    /// The titles with the most counted posts over the featured window, shared by the hero and the search suggestions.
    /// The counts change only when titles are pulled, so they are cached for a while.
    /// </summary>
    private async Task<IReadOnlyList<TalkedAbout>> MostTalkedAboutAsync(CancellationToken ct) =>
        await _cache.GetOrCreateAsync("home:most-talked-about", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TalkedAboutCacheFor;
            return _queries.GetMostTalkedAboutAsync(DateTime.UtcNow.AddDays(-WindowDays), Math.Max(1, _options.FeaturedMinPosts), 30, ct);
        }) ?? Array.Empty<TalkedAbout>();

    /// <summary>
    /// The hero: the tracked movie or show people are actually posting about (see <see cref="FeaturedPick"/>), else TMDB's
    /// trending pick, labelled as TMDB's.
    /// </summary>
    private async Task<(Title? Title, string Label, int? Posts)> PickFeaturedAsync(IReadOnlyList<Title> trending, CancellationToken ct)
    {
        try
        {
            var candidates = await MostTalkedAboutAsync(ct);
            var tracked = new HashSet<(MediaType, int)>();
            foreach (var c in candidates)
                if (await _tracker.GetReasonAsync(c.MediaType, c.TmdbId, ct) is not null)
                    tracked.Add((c.MediaType, c.TmdbId));

            foreach (var pick in FeaturedPick.Eligible(candidates, c => tracked.Contains((c.MediaType, c.TmdbId)),
                         _options.FeaturedMinPosts, _options.FeaturedMinConfidence))
            {
                var title = await _titles.GetDetailsAsync(pick.MediaType, pick.TmdbId, ct);
                if (title is { BackdropPath.Length: > 0 })
                    return (title, FeaturedPick.MostTalkedAbout, pick.Posts);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A slow or paused database must not take the home page down; TMDB's pick still works.
            _logger.LogWarning(ex, "Most-talked-about lookup failed; featuring TMDB's trending pick");
        }

        var fallback = trending.FirstOrDefault(t => t.BackdropPath.Length > 0) ?? trending.FirstOrDefault();
        return (fallback, FeaturedPick.TrendingOnTmdb, null);
    }

    private sealed record MoverRows(IReadOnlyList<Mover> Rising, IReadOnlyList<Mover> Falling);

    /// <summary>The same trend test as the title page (window, alpha and minimum days), across every well-measured title.</summary>
    private async Task<MoverRows> MoversAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-_options.TrendWindowDays + 1);
        if (_options.ScoringChangedOn is { } changed && changed > since) since = changed;   // days scored by an older formula don't count
        var series = await _queries.GetScoreSeriesAsync(since, _home.MoverMinPosts, ct);
        var (rising, falling) = HomeSignals.Movers(series, _options.TrendAlpha, _options.MinTrendPoints, _home.SignalRowSize);
        return new MoverRows(rising, falling);
    }

    /// <summary>A signal row from title keys: their details are fetched side by side (TMDB only); any that fail are skipped.</summary>
    private async Task<IReadOnlyList<HomeRowDto>> SignalRowAsync(string key, string title, string subtitle,
        IEnumerable<(MediaType MediaType, int TmdbId)> keys, CancellationToken ct)
    {
        var titles = await Task.WhenAll(keys.Select(k => DetailsOrNullAsync(k.MediaType, k.TmdbId, ct)));
        return Row(key, title, subtitle, titles.OfType<Title>().ToList());
    }

    private async Task<Title?> DetailsOrNullAsync(MediaType mediaType, int tmdbId, CancellationToken ct)
    {
        try { return await _titles.GetDetailsAsync(mediaType, tmdbId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    /// <summary>One database step; a failure is logged and its rows are left out rather than failing the page.</summary>
    private async Task<T?> ReadAsync<T>(string what, Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Home page: {What} failed; leaving those rows out", what);
            return default;
        }
    }

    private static IReadOnlyList<HomeRowDto> Row(string key, string title, string? subtitle, IReadOnlyList<Title> items) =>
        new[] { new HomeRowDto { Key = key, Title = title, Subtitle = subtitle, Items = items } };

    /// <summary>Steam is a second provider; if it is down the home page should still render the TMDB rows.</summary>
    private static async Task<IReadOnlyList<Title>> SteamAsync(Task<PagedResult<Title>> pending)
    {
        try { return (await pending).Items; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Array.Empty<Title>(); }
    }
}
