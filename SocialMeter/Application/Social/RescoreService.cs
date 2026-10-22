using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Common;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// Operator use case: re-reads stored posts with the current assessor and trust rules and recomputes every cached
/// reading, without contacting a source. One run at a time: two would double the model bill and race on the same rows.
/// </summary>
public sealed class RescoreService : IRescoreService
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private readonly SocialMeterDbContext _db;
    private readonly ITitleService _titles;
    private readonly PostIngestionService _ingestion;
    private readonly ScoreHistoryService _history;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<RescoreService> _logger;

    public RescoreService(SocialMeterDbContext db, ITitleService titles, PostIngestionService ingestion, ScoreHistoryService history, IOptions<SocialMeterOptions> options, ILogger<RescoreService> logger)
    {
        _db = db;
        _titles = titles;
        _ingestion = ingestion;
        _history = history;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RescoreAsync(bool force, CancellationToken ct = default)
    {
        if (!await RunLock.WaitAsync(0, ct))
            throw new OperationInProgressException("Rescore");
        try
        {
            return await RunAsync(force, ct);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<int> RunAsync(bool force, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var assessed = 0;
        var total = 0;

        // One title at a time keeps memory flat however many posts are stored.
        var titleKeys = await _db.SocialPosts.Select(p => new { p.MediaType, p.TmdbId }).Distinct().ToListAsync(ct);
        foreach (var key in titleKeys)
        {
            var titlePosts = await _db.SocialPosts.Where(p => p.MediaType == key.MediaType && p.TmdbId == key.TmdbId).ToListAsync(ct);
            total += titlePosts.Count;

            var toAssess = titlePosts.Where(p => force || p.Analyzer != _ingestion.AnalyzerFor(p)).ToList();
            if (toAssess.Count > 0)
            {
                var title = await _titles.GetDetailsAsync(key.MediaType, key.TmdbId, ct)
                            ?? new Title { TmdbId = key.TmdbId, MediaType = key.MediaType, Name = "" };
                await _ingestion.AssessAndApplyAsync(title, toAssess, now, ct);
                assessed += toAssess.Count;
            }

            TrustPolicy.ApplyCrossPostRules(titlePosts, now);
            var meter = SocialScoreCalculator.Compute(titlePosts, now, _options.Scoring);

            var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == key.TmdbId && s.MediaType == key.MediaType, ct);
            if (snapshot is not null)
            {
                snapshot.ApplyMeter(meter, snapshot.LastRefreshedAt);
                await _history.RecordAsync(key.MediaType, key.TmdbId, meter, now, ct);
            }

            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Rescored {Assessed} of {Total} posts with {Assessor}", assessed, total, _ingestion.AssessorName);
        return assessed;
    }
}
