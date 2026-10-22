using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Imdb;
using SocialMeter.Infrastructure.Links;
using SocialMeter.Infrastructure.Omdb;

namespace SocialMeter.Application.Ratings;

public sealed class RatingsService : IRatingsService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(5);

    private readonly OmdbClient _omdb;
    private readonly RatingLinkResolver _links;
    private readonly ImdbRatingsDataset _imdb;
    private readonly PublishedScoreReader _published;
    private readonly SocialMeterDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RatingsService> _logger;

    public RatingsService(OmdbClient omdb, RatingLinkResolver links, ImdbRatingsDataset imdb, PublishedScoreReader published,
        SocialMeterDbContext db, IMemoryCache cache, ILogger<RatingsService> logger)
    {
        _omdb = omdb;
        _links = links;
        _imdb = imdb;
        _published = published;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExternalRating>> GetRatingsAsync(Title title, CancellationToken ct = default)
    {
        var key = $"ratings:{title.MediaType}:{title.TmdbId}";
        if (_cache.TryGetValue(key, out IReadOnlyList<ExternalRating>? cached) && cached is not null)
            return cached;

        if (title.MediaType == MediaType.Game)
        {
            var game = await GameRatingsAsync(title, ct);
            _cache.Set(key, game, CacheFor);
            return game;
        }

        var ratings = new List<ExternalRating>
        {
            new()
            {
                Source = "tmdb",
                DisplayName = "TMDB",
                Value = title.VoteCount > 0 ? $"{title.VoteAverage:0.0}/10" : null,
                Normalized = title.VoteCount > 0 ? (int)Math.Round(title.VoteAverage * 10) : null,
                Detail = title.VoteCount > 0 ? $"{title.VoteCount:N0} votes" : "No votes yet",
                Url = $"https://www.themoviedb.org/{title.MediaType.Segment()}/{title.TmdbId}"
            }
        };

        OmdbTitle? omdb = null;
        var omdbFailed = false;
        if (_omdb.IsConfigured && !string.IsNullOrWhiteSpace(title.ImdbId))
        {
            try
            {
                omdb = await _omdb.GetByImdbIdAsync(title.ImdbId, ct);
            }
            catch (Exception ex)
            {
                omdbFailed = true;
                _logger.LogWarning("OMDb lookup failed for {ImdbId}: {Message}", title.ImdbId, ex.Message);
            }
        }

        var imdbUrl = title.ImdbId is null ? null : $"https://www.imdb.com/title/{title.ImdbId}/";
        var rt = omdb?.Ratings?.FirstOrDefault(r => r.Source == "Rotten Tomatoes")?.Value;

        // Rotten Tomatoes and Metacritic expose no id, so the resolver guesses and verifies their page URLs.
        var rtTask = _links.RottenTomatoesAsync(title, ct);
        var mcTask = _links.MetacriticAsync(title, ct);
        await Task.WhenAll(rtTask, mcTask);
        var rtUrl = rtTask.Result;
        var mcUrl = mcTask.Result;

        // IMDb: IMDb's own daily file first (it has new releases days or weeks before OMDb does), then OMDb.
        if (_imdb.TryGet(title.ImdbId, out var imdbRating, out var imdbVotes))
            ratings.Add(Build("imdb", "IMDb", imdbRating.ToString("0.0", CultureInfo.InvariantCulture), (int)Math.Round(imdbRating * 10),
                $"{imdbVotes:N0} votes", imdbUrl, "/10"));
        else
            ratings.Add(Build("imdb", "IMDb", omdb?.ImdbRating, ParseOutOfTen(omdb?.ImdbRating),
                omdb?.ImdbVotes is { } v && v != "N/A" ? $"{v} votes" : null, imdbUrl, "/10"));

        // Rotten Tomatoes and Metacritic: OMDb when it has them, otherwise the score the site publishes on the title's page.
        // Only the critics' score counts; Rotten Tomatoes series pages carry only an audience score and stay "Not available".
        if (ParsePercent(rt) is null && await _published.ReadAsync(rtUrl, "Tomatometer", title.Year, ct) is { } rtPage)
            ratings.Add(Build("rottenTomatoes", "Rotten Tomatoes", $"{rtPage.Value}%", rtPage.Value, Reviews("Tomatometer", rtPage.Count, "reviews"), rtUrl, ""));
        else
            ratings.Add(Build("rottenTomatoes", "Rotten Tomatoes", rt, ParsePercent(rt), "Tomatometer", rtUrl, ""));

        if (ParseInt(omdb?.Metascore) is null && await _published.ReadAsync(mcUrl, "Metascore", title.Year, ct) is { } mcPage)
            ratings.Add(Build("metacritic", "Metacritic", mcPage.Value.ToString(CultureInfo.InvariantCulture), mcPage.Value, Reviews("Metascore", mcPage.Count, "critics"), mcUrl, "/100"));
        else
            ratings.Add(Build("metacritic", "Metacritic", omdb?.Metascore, ParseInt(omdb?.Metascore), "Metascore", mcUrl, "/100"));

        if (!_omdb.IsConfigured)
            _logger.LogDebug("OMDb not configured; IMDb/RT/Metacritic ratings unavailable.");

        // A failed lookup (key not yet active, daily quota hit), or IMDb's file still loading after a restart, is retried
        // soon rather than frozen for the full window.
        var imdbLoading = _imdb.Count == 0 && title.ImdbId is not null;
        _cache.Set(key, (IReadOnlyList<ExternalRating>)ratings, omdbFailed || imdbLoading ? RetryFailedAfter : CacheFor);
        return ratings;
    }

    /// <summary>
    /// Games: Steam's all-time score (on the title), a 30-day score computed from the recent reviews SocialMeter
    /// has read (Steam's summary endpoint ignores date ranges), and Metacritic via the URL Steam supplies.
    /// </summary>
    private async Task<IReadOnlyList<ExternalRating>> GameRatingsAsync(Title title, CancellationToken ct)
    {
        var steamUrl = $"https://store.steampowered.com/app/{title.TmdbId}/#app_reviews_hash";
        var hasReviews = title.SteamReviewPercent is not null && title.SteamReviewCount > 0;

        var since = DateTime.UtcNow.AddDays(-30);
        var recent = await _db.SocialPosts
            .Where(p => p.Platform == "steam" && p.MediaType == MediaType.Game && p.TmdbId == title.TmdbId
                        && p.VerdictUp != null && p.CreatedAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Up = g.Count(p => p.VerdictUp == true) })
            .FirstOrDefaultAsync(ct);

        var hasRecent = recent is { Total: >= 5 };
        var recentPct = hasRecent ? (int)Math.Round(100.0 * recent!.Up / recent.Total) : (int?)null;

        return new List<ExternalRating>
        {
            new()
            {
                Source = "steam",
                DisplayName = "Steam all-time",
                Value = hasReviews ? $"{title.SteamReviewPercent}%" : null,
                Normalized = hasReviews ? title.SteamReviewPercent : null,
                Detail = hasReviews ? $"{title.SteamReviewLabel} · {title.SteamReviewCount:N0} reviews" : "No reviews yet",
                Url = steamUrl
            },
            new()
            {
                Source = "steamRecent",
                DisplayName = "Steam last 30 days",
                Value = hasRecent ? $"{recentPct}%" : null,
                Normalized = recentPct,
                Detail = hasRecent ? $"{recent!.Up:N0} of the {recent.Total:N0} newest reviews we read" : "Not enough recent reviews read yet",
                Url = steamUrl
            },
            new()
            {
                Source = "metacritic",
                DisplayName = "Metacritic",
                Value = title.MetacriticScore is { } m ? $"{m}/100" : null,
                Normalized = title.MetacriticScore,
                Detail = title.MetacriticScore is null ? "Not available" : "Metascore",
                Url = title.MetacriticUrl ?? $"https://www.metacritic.com/search/{Uri.EscapeDataString(title.Name)}/"
            }
        };
    }

    private static ExternalRating Build(string source, string name, string? raw, int? normalized, string? detail, string? url, string suffix)
    {
        var available = normalized is not null && raw is not null && raw != "N/A";
        return new ExternalRating
        {
            Source = source,
            DisplayName = name,
            Value = available ? (raw!.EndsWith('%') || suffix.Length == 0 ? raw : raw + suffix) : null,
            Normalized = available ? normalized : null,
            Detail = available ? detail : "Not available",
            Url = url
        };
    }

    private static string Reviews(string label, int? count, string noun)
        => count is > 0 ? $"{label} · {count:N0} {noun}" : label;

    private static int? ParseOutOfTen(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d * 10) : null;

    private static int? ParsePercent(string? s)
        => s is not null && s.EndsWith('%') && int.TryParse(s[..^1], out var i) ? i : null;

    private static int? ParseInt(string? s)
        => int.TryParse(s, out var i) ? i : null;
}
