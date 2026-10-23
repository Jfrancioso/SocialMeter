using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

/// <summary>Who asked for a reading. Stored in RefreshLogs.Trigger as "page", "button" or "watch".</summary>
public enum ReadTrigger
{
    /// <summary>Someone opened the title; the sources are read only when the cached reading is stale.</summary>
    Page,

    /// <summary>Someone pressed Refresh: read now unless the title was read in the last few minutes.</summary>
    Button,

    /// <summary>A watch window's scheduled read (WatchWindowWorker); behaves like Refresh.</summary>
    Watch
}

/// <summary>The reading for one title, refreshing from the social sources when it is due and allowed.</summary>
public interface ISocialMeterService
{
    Task<SocialMeterResponseDto?> GetScoreAsync(MediaType mediaType, int tmdbId, ReadTrigger trigger, CancellationToken ct = default);
}

/// <summary>The numbers behind a cached reading. Never contacts the sources.</summary>
public interface IScoreExplainer
{
    Task<ScoreExplanationDto?> ExplainAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default);
}

/// <summary>Read-only queries over stored posts, cached readings and daily history. Never contacts the sources.</summary>
public interface IPostQueries
{
    Task<PagedResult<SocialPostDto>> GetPostsAsync(MediaType mediaType, int tmdbId, Sentiment? sentiment, string? platform, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Cached readings only. Keys look like "movie:693134".</summary>
    Task<IReadOnlyDictionary<string, SnapshotDto>> GetSnapshotsAsync(IEnumerable<(MediaType MediaType, int TmdbId)> keys, CancellationToken ct = default);

    /// <summary>Titles that have a reading (at least one counted post), most recently read first. For the sitemap.</summary>
    Task<IReadOnlyList<SocialMeter.Contracts.Seo.SitemapTitleDto>> GetScoredTitlesAsync(int take, CancellationToken ct = default);

    Task<ScoreHistoryDto> GetHistoryAsync(MediaType mediaType, int tmdbId, int days, CancellationToken ct = default);

    /// <summary>Titles by counted posts created since <paramref name="since"/>, most first, with their reading's confidence.</summary>
    Task<IReadOnlyList<SocialMeter.Application.Home.TalkedAbout>> GetMostTalkedAboutAsync(DateTime since, int minPosts, int take, CancellationToken ct = default);

    /// <summary>Every scored movie and show with a stored critics score.</summary>
    Task<IReadOnlyList<SocialMeter.Application.Home.CrowdCritics>> GetCrowdCriticsAsync(CancellationToken ct = default);

    /// <summary>Daily scores since <paramref name="since"/> for titles with at least <paramref name="minPosts"/> counted posts.</summary>
    Task<IReadOnlyList<(MediaType MediaType, int TmdbId, IReadOnlyList<(DateOnly Day, int Score)> Days)>> GetScoreSeriesAsync(DateOnly since, int minPosts, CancellationToken ct = default);
}

/// <summary>
/// Re-assesses stored posts and recomputes every snapshot, without contacting the sources. By default only posts the
/// current assessor hasn't read are sent to it (cheap); <c>force</c> re-assesses everything. Returns the number assessed.
/// </summary>
public interface IRescoreService
{
    Task<int> RescoreAsync(bool force, CancellationToken ct = default);
}
