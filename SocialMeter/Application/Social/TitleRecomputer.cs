using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// Recomputes one title's score from the posts already stored, with no fetching and no model calls: re-runs the
/// cross-post rules, updates the snapshot and today's history row. Used after posts are purged or hidden. Does not save.
/// </summary>
public sealed class TitleRecomputer
{
    private readonly SocialMeterDbContext _db;
    private readonly ScoreHistoryService _history;
    private readonly SocialMeterOptions _options;

    public TitleRecomputer(SocialMeterDbContext db, ScoreHistoryService history, IOptions<SocialMeterOptions> options)
    {
        _db = db;
        _history = history;
        _options = options.Value;
    }

    /// <summary>The new reading, or null when the title has never been scored (no snapshot to update).</summary>
    public async Task<SocialMeterScore?> RecomputeAsync(MediaType mediaType, int tmdbId, DateTime nowUtc, CancellationToken ct = default)
    {
        var posts = await _db.SocialPosts.Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType).ToListAsync(ct);
        TrustPolicy.ApplyCrossPostRules(posts, nowUtc);
        var meter = SocialScoreCalculator.Compute(posts, nowUtc, _options.Scoring);

        var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
        if (snapshot is null) return null;

        snapshot.ApplyMeter(meter, snapshot.LastRefreshedAt);
        await _history.RecordAsync(mediaType, tmdbId, meter, nowUtc, ct);
        return meter;
    }
}
