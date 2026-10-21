using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface ISocialMeterService
{
    /// <summary>Returns the title's SocialMeter reading, refreshing from the social sources when the cached one is stale.</summary>
    Task<SocialMeterResponseDto?> GetScoreAsync(MediaType mediaType, int tmdbId, bool forceRefresh, CancellationToken ct = default);

    Task<PagedResult<SocialPostDto>> GetPostsAsync(MediaType mediaType, int tmdbId, Sentiment? sentiment, string? platform, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Cached readings only; never triggers a fetch. Keys look like "movie:693134".</summary>
    Task<IReadOnlyDictionary<string, SnapshotDto>> GetSnapshotsAsync(IEnumerable<(MediaType MediaType, int TmdbId)> keys, CancellationToken ct = default);

    /// <summary>
    /// Re-assesses stored posts and recomputes every snapshot, without contacting the sources.
    /// By default only posts the current assessor hasn't read yet are sent to it (cheap); <paramref name="force"/>
    /// re-assesses everything. Returns the number of posts assessed.
    /// </summary>
    Task<int> RescoreAsync(bool force, CancellationToken ct = default);

    /// <summary>The numbers behind the cached reading: weight shares, substance mix, top drivers. Never contacts the sources.</summary>
    Task<ScoreExplanationDto?> ExplainAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default);

    /// <summary>Daily readings and the trend verdict for the last <paramref name="days"/> days.</summary>
    Task<ScoreHistoryDto> GetHistoryAsync(MediaType mediaType, int tmdbId, int days, CancellationToken ct = default);
}
