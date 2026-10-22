using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// The "give me this title's reading" use case. Decides whether a live pull is due (tracked, stale or forced, inside
/// budget), runs it under the per-title gate, recomputes the score and keeps the cached snapshot and today's history
/// row in line with what the page is about to show.
/// </summary>
public sealed class SocialMeterService : ISocialMeterService
{
    private readonly SocialMeterDbContext _db;
    private readonly ITitleService _titles;
    private readonly ITitleTracker _tracker;
    private readonly IEnumerable<ISocialSource> _sources;
    private readonly PostIngestionService _ingestion;
    private readonly ScoreHistoryService _history;
    private readonly PullGate _gate;
    private readonly LivePullBudget _budget;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<SocialMeterService> _logger;

    public SocialMeterService(
        SocialMeterDbContext db,
        ITitleService titles,
        ITitleTracker tracker,
        IEnumerable<ISocialSource> sources,
        PostIngestionService ingestion,
        ScoreHistoryService history,
        PullGate gate,
        LivePullBudget budget,
        IOptions<SocialMeterOptions> options,
        ILogger<SocialMeterService> logger)
    {
        _db = db;
        _titles = titles;
        _tracker = tracker;
        _sources = sources;
        _ingestion = ingestion;
        _history = history;
        _gate = gate;
        _budget = budget;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SocialMeterResponseDto?> GetScoreAsync(MediaType mediaType, int tmdbId, bool forceRefresh, CancellationToken ct = default)
    {
        var title = await _titles.GetDetailsAsync(mediaType, tmdbId, ct);
        if (title is null)
            return null;

        // The gate that bounds the bill: titles outside the tracked set never trigger a pull.
        var trackedReason = await _tracker.GetReasonAsync(mediaType, tmdbId, ct);
        var tracked = trackedReason is not null;
        var now = DateTime.UtcNow;

        // Held until the snapshot is saved, so a request that waited here reads what the previous one wrote.
        using var _ = await _gate.AcquireAsync(mediaType, tmdbId, ct);

        var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
        var (pull, sourceStatus, outcome) = await DecideAndPullAsync(title, snapshot, tracked, forceRefresh, now, ct);

        var posts = await _db.SocialPosts.Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType).ToListAsync(ct);
        if (pull)
        {
            // Rules that look across the whole set (one voice per account, duplicates) run once everything is in.
            TrustPolicy.ApplyCrossPostRules(posts, now);
        }

        var meter = SocialScoreCalculator.Compute(posts, now, _options.Scoring);
        snapshot = await PersistAsync(snapshot, meter, mediaType, tmdbId, pull, sourceStatus, now, ct);
        if (forceRefresh || pull)
            await LogRefreshAsync(title, forceRefresh, pull, outcome, sourceStatus, now, ct);

        var counted = posts.Where(p => !p.Excluded).ToList();
        var totalWeight = counted.Sum(p => SocialScoreCalculator.Weight(p, now));
        var top = counted
            .OrderByDescending(p => SocialScoreCalculator.Weight(p, now))
            .Take(_options.TopPosts)
            .Select(p => SocialPostDto.From(p, SocialScoreCalculator.Share(p, totalWeight, now)))
            .ToList();

        return new SocialMeterResponseDto
        {
            TmdbId = tmdbId,
            MediaType = mediaType,
            TitleName = title.Name,
            Tracked = tracked,
            TrackedReason = trackedReason,
            Meter = meter,
            TopPosts = top,
            Sources = sourceStatus,
            LastRefreshedAt = snapshot?.LastRefreshedAt ?? now,
            FromCache = !pull,
            NextRefreshAllowedAt = (snapshot?.LastRefreshedAt ?? now).AddMinutes(_options.MinForcedRefreshMinutes)
        };
    }

    /// <summary>Whether to pull now, the per-source outcome either way, and the outcome word for the refresh log.</summary>
    private async Task<(bool Pulled, List<SourceStatusDto> Status, string Outcome)> DecideAndPullAsync(Title title, TitleSnapshot? snapshot, bool tracked, bool forceRefresh, DateTime now, CancellationToken ct)
    {
        var sinceRefresh = snapshot is null ? TimeSpan.MaxValue : now - snapshot.LastRefreshedAt;
        var stale = sinceRefresh > TimeSpan.FromHours(_options.RefreshAfterHours);
        var recentlyPulled = sinceRefresh < TimeSpan.FromMinutes(_options.MinForcedRefreshMinutes);
        var decision = PullPolicy.Decide(tracked, stale, forceRefresh, recentlyPulled);

        if (decision != PullDecision.Pull)
            return (false, Idle(null), PullPolicy.OutcomeName(decision, budgetRefused: false));

        if (!_budget.TryAcquirePull())
        {
            // The hourly ceiling is the last line against a runaway bill; the reader sees the cache and the reason.
            _logger.LogWarning("Live pull budget exhausted; serving cached reading for {Type} {Id}", title.MediaType, title.TmdbId);
            return (false, Idle("Live reads paused: hourly budget reached"), PullPolicy.OutcomeName(decision, budgetRefused: true));
        }

        var status = new List<SourceStatusDto>();
        foreach (var source in _sources)
            status.Add(await _ingestion.IngestAsync(source, title, now, ct));
        return (true, status, PullPolicy.OutcomeName(decision, budgetRefused: false));
    }

    /// <summary>
    /// Notes a Refresh press or a pull in RefreshLogs: title, time, trigger and outcome, never who asked. Only requests
    /// that already used the database get here (per-visitor refusals are turned away earlier, in the controller), so
    /// logging adds one small row to work already under way and never wakes a paused database. A failed write is logged
    /// and ignored: the reading the reader asked for matters more than the record of it.
    /// </summary>
    private async Task LogRefreshAsync(Title title, bool forceRefresh, bool pulled, string outcome, List<SourceStatusDto> status, DateTime now, CancellationToken ct)
    {
        var entry = _db.RefreshLogs.Add(new RefreshLog
        {
            At = now,
            MediaType = title.MediaType,
            TmdbId = title.TmdbId,
            TitleName = title.Name.Length > 200 ? title.Name[..200] : title.Name,
            Trigger = forceRefresh ? "button" : "page",
            Pulled = pulled,
            Outcome = outcome,
            PostsFetched = pulled ? status.Sum(s => s.Fetched ?? 0) : null
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            entry.State = EntityState.Detached;
            _logger.LogWarning(ex, "Could not record refresh for {Type} {Id}", title.MediaType, title.TmdbId);
        }
    }

    private List<SourceStatusDto> Idle(string? note) => _sources
        .Select(s => new SourceStatusDto { Platform = s.Platform, Configured = s.IsConfigured, Error = s.IsConfigured ? note : null })
        .ToList();

    /// <summary>Writes the snapshot after a pull, or re-syncs it when the maths changed since it was written.</summary>
    private async Task<TitleSnapshot?> PersistAsync(TitleSnapshot? snapshot, SocialMeterScore meter, MediaType mediaType, int tmdbId, bool pulled, List<SourceStatusDto> status, DateTime now, CancellationToken ct)
    {
        if (pulled)
        {
            snapshot ??= _db.TitleSnapshots.Add(new TitleSnapshot { TmdbId = tmdbId, MediaType = mediaType }).Entity;

            // When every configured source failed, retry in ten minutes instead of caching the failure for the full window.
            var configured = status.Where(s => s.Configured).ToList();
            var allFailed = configured.Count > 0 && configured.All(s => s.Error is not null);
            snapshot.ApplyMeter(meter, allFailed ? now.AddHours(-_options.RefreshAfterHours).AddMinutes(10) : now);
            await _history.RecordAsync(mediaType, tmdbId, meter, now, ct);
            await _db.SaveChangesAsync(ct);
        }
        else if (snapshot is not null && (snapshot.Score != meter.Score || snapshot.Margin != meter.Stats.Margin || snapshot.TotalPosts != meter.TotalPosts))
        {
            // The maths changed (or posts were re-read) since the snapshot was written: bring the cache and today's history
            // row in line with what the page is about to show, without pretending the sources were contacted.
            snapshot.ApplyMeter(meter, snapshot.LastRefreshedAt);
            await _history.RecordAsync(mediaType, tmdbId, meter, now, ct);
            await _db.SaveChangesAsync(ct);
        }
        return snapshot;
    }
}
