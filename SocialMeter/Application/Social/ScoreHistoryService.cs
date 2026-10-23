using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// Keeps one reading per title per day and runs the trend test over them. Today's row is rewritten whenever the
/// title is rescored; past days are never touched except by the one-off backfill that rebuilds them from post dates
/// when a title has no history yet.
/// </summary>
public sealed class ScoreHistoryService
{
    private readonly SocialMeterDbContext _db;
    private readonly PullGate _gate;
    private readonly SocialMeterOptions _options;

    public ScoreHistoryService(SocialMeterDbContext db, PullGate gate, IOptions<SocialMeterOptions> options)
    {
        _db = db;
        _gate = gate;
        _options = options.Value;
    }

    /// <summary>Upserts today's row from a freshly computed reading. Does not save.</summary>
    public async Task RecordAsync(MediaType mediaType, int tmdbId, SocialMeterScore meter, DateTime nowUtc, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(nowUtc);
        var row = await _db.ScoreHistory.SingleOrDefaultAsync(h => h.TmdbId == tmdbId && h.MediaType == mediaType && h.Day == day, ct)
                  ?? _db.ScoreHistory.Add(new ScoreHistory { TmdbId = tmdbId, MediaType = mediaType, Day = day }).Entity;

        row.Score = meter.Score;
        row.RawScore = meter.Stats.RawScore;
        row.EffectivePosts = meter.Stats.EffectivePosts;
        row.Margin = meter.Stats.Margin;
        row.TotalPosts = meter.TotalPosts;
        row.Reconstructed = false;
        row.RecordedAt = nowUtc;
    }

    /// <summary>
    /// The daily series and the trend verdict. A title with no history gets its past rebuilt from post dates first, and a
    /// missing row for today is filled from the current snapshot, so the first visit already shows a line.
    /// </summary>
    public async Task<ScoreHistoryDto> GetAsync(MediaType mediaType, int tmdbId, int days, DateTime nowUtc, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);
        var today = DateOnly.FromDateTime(nowUtc);

        // Filling in missing rows writes, and two visitors (or a visitor and a live pull) can arrive for the same title at
        // once; both would insert the same day and one would fail on the unique index. So the fill runs under the title's
        // pull gate, taken only if it is free right now: if it is busy, this request just reads. Never wait for it, since
        // a pull can hold it for half a minute; the pull records today's row itself and the next visit fills the rest.
        using (var gate = _gate.TryAcquire(mediaType, tmdbId))
        {
            if (gate is not null)
                await FillMissingAsync(mediaType, tmdbId, today, nowUtc, ct);
        }

        var since = today.AddDays(-days);
        var rows = await _db.ScoreHistory
            .Where(h => h.TmdbId == tmdbId && h.MediaType == mediaType && h.Day > since)
            .OrderBy(h => h.Day)
            .ToListAsync(ct);

        var points = rows.Select(r => new ScorePointDto
        {
            Day = r.Day,
            Score = r.Score,
            RawScore = r.RawScore,
            Margin = r.Margin,
            EffectivePosts = r.EffectivePosts,
            TotalPosts = r.TotalPosts,
            Reconstructed = r.Reconstructed
        }).ToList();

        var smoothed = ScoreStatistics.Ewma(points.Select(p => (double)p.Score).ToList(), 0.3);
        for (var i = 0; i < points.Count; i++) points[i].Smoothed = Math.Round(smoothed[i], 1);

        var trendRows = rows.Where(r => r.Day > today.AddDays(-_options.TrendWindowDays)).ToList();
        var series = trendRows.Select(r => ((double)r.Day.DayNumber, (double)r.Score)).ToList();
        var test = ScoreStatistics.MannKendall(series, _options.TrendAlpha, _options.MinTrendPoints);

        return new ScoreHistoryDto
        {
            Points = points,
            Trend = new TrendDto
            {
                Direction = test.Direction,
                Points = test.Points,
                PointsNeeded = _options.MinTrendPoints,
                WindowDays = _options.TrendWindowDays,
                PValue = Math.Round(test.PValue, 3),
                SlopePerDay = Math.Round(test.Slope, 2),
                Change = trendRows.Count >= 2 ? trendRows[^1].Score - trendRows[0].Score : null
            }
        };
    }

    private async Task FillMissingAsync(MediaType mediaType, int tmdbId, DateOnly today, DateTime nowUtc, CancellationToken ct)
    {
        var any = await _db.ScoreHistory.AnyAsync(h => h.TmdbId == tmdbId && h.MediaType == mediaType, ct);
        if (!any)
            await BackfillAsync(mediaType, tmdbId, nowUtc, ct);

        var hasToday = await _db.ScoreHistory.AnyAsync(h => h.TmdbId == tmdbId && h.MediaType == mediaType && h.Day == today, ct);
        if (!hasToday)
        {
            var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
            if (snapshot is not null && snapshot.TotalPosts > 0)
            {
                _db.ScoreHistory.Add(new ScoreHistory
                {
                    TmdbId = tmdbId, MediaType = mediaType, Day = today,
                    Score = snapshot.Score, RawScore = snapshot.RawScore, EffectivePosts = snapshot.EffectivePosts,
                    Margin = snapshot.Margin, TotalPosts = snapshot.TotalPosts, Reconstructed = false, RecordedAt = nowUtc
                });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Rebuilds past days from the posts on file: for each day, the reading the current maths gives to the posts that existed
    /// by then. Engagement and trust are today's, so rows are flagged Reconstructed. Does not save.
    /// </summary>
    public async Task BackfillAsync(MediaType mediaType, int tmdbId, DateTime nowUtc, CancellationToken ct = default)
    {
        var posts = await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded)
            .ToListAsync(ct);
        if (posts.Count == 0) return;

        var today = DateOnly.FromDateTime(nowUtc);
        var firstDay = DateOnly.FromDateTime(posts.Min(p => p.CreatedAt));
        var start = firstDay < today.AddDays(-_options.HistoryBackfillDays) ? today.AddDays(-_options.HistoryBackfillDays) : firstDay;

        for (var day = start; day < today; day = day.AddDays(1))
        {
            var asOf = day.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
            var visible = posts.Where(p => p.CreatedAt <= asOf).ToList();
            if (visible.Count == 0) continue;

            var reading = SocialScoreCalculator.Read(visible, asOf, _options.Scoring);
            _db.ScoreHistory.Add(new ScoreHistory
            {
                TmdbId = tmdbId,
                MediaType = mediaType,
                Day = day,
                Score = reading.Score,
                RawScore = reading.RawScore,
                EffectivePosts = Math.Round(reading.EffectivePosts, 1),
                Margin = reading.Margin,
                TotalPosts = visible.Count,
                Reconstructed = true,
                RecordedAt = nowUtc
            });
        }
    }
}
