using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Ratings;

/// <summary>
/// Keeps each scored movie's and show's critics baseline (see <see cref="CriticsBaseline"/>) on its snapshot, so cards and
/// the "Crowd vs critics" row can show it without fetching ratings for dozens of titles at once. Updated whenever a title
/// page loads its ratings, and in the background for titles nobody has opened lately (<see cref="CriticsRefreshWorker"/>).
/// </summary>
public sealed class CriticsScores
{
    /// <summary>A stored critics score is refreshed after this long.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    private readonly SocialMeterDbContext _db;
    private readonly ITitleService _titles;
    private readonly IRatingsService _ratings;
    private readonly ILogger<CriticsScores> _logger;

    public CriticsScores(SocialMeterDbContext db, ITitleService titles, IRatingsService ratings, ILogger<CriticsScores> logger)
    {
        _db = db;
        _titles = titles;
        _ratings = ratings;
        _logger = logger;
    }

    /// <summary>
    /// Stores a freshly computed baseline on the title's snapshot, if it has one. A missing value never erases a known one
    /// (a source being down for an hour shouldn't blank the card). Writes at most once a day per title.
    /// Best effort: it runs inside the ratings request, and a failed save must never turn the ratings into an error.
    /// </summary>
    public async Task RecordAsync(MediaType mediaType, int tmdbId, int? score, DateTime nowUtc, CancellationToken ct = default)
    {
        if (mediaType == MediaType.Game) return;
        try
        {
            var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
            if (snapshot is null) return;

            var newScore = score ?? snapshot.CriticsScore;          // a missing value keeps the known one
            var unchanged = snapshot.CriticsScore == newScore;
            var checkedToday = snapshot.CriticsCheckedAt > nowUtc.AddDays(-1);
            if (unchanged && checkedToday) return;

            snapshot.CriticsScore = newScore;
            snapshot.CriticsCheckedAt = nowUtc;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not store the critics score for {Type} {Id}", mediaType, tmdbId);
        }
    }

    /// <summary>
    /// Refreshes the critics score of up to <paramref name="take"/> scored movies and shows whose stored value is missing
    /// or stale, the most-discussed first, one at a time with a pause so the ratings sites are asked politely.
    /// </summary>
    public async Task<int> RefreshDueAsync(int take, DateTime nowUtc, CancellationToken ct = default)
    {
        var staleBefore = nowUtc - StaleAfter;
        var due = await _db.TitleSnapshots
            .Where(s => s.MediaType != MediaType.Game && s.TotalPosts > 0 && (s.CriticsCheckedAt == null || s.CriticsCheckedAt < staleBefore))
            .OrderByDescending(s => s.TotalPosts)
            .Select(s => new { s.MediaType, s.TmdbId })
            .Take(take)
            .ToListAsync(ct);

        var done = 0;
        foreach (var d in due)
        {
            try
            {
                var title = await _titles.GetDetailsAsync(d.MediaType, d.TmdbId, ct);
                if (title is null) continue;
                var (score, _) = CriticsBaseline.For(d.MediaType, await _ratings.GetRatingsAsync(title, ct));
                await RecordAsync(d.MediaType, d.TmdbId, score, nowUtc, ct);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Critics refresh failed for {Type} {Id}", d.MediaType, d.TmdbId);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return done;
    }
}
