using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Common;
using SocialMeter.Application.Guardrails;
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

    public async Task<SocialMeterResponseDto?> GetScoreAsync(MediaType mediaType, int tmdbId, ReadTrigger trigger, CancellationToken ct = default)
    {
        var forceRefresh = trigger != ReadTrigger.Page;
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
        var attempt = await DecideAndPullAsync(title, snapshot, tracked, forceRefresh, now, ct);
        var pull = attempt.Pulled;
        var paused = attempt.Refusal != PullRefusal.None;

        var posts = await _db.SocialPosts.Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType).ToListAsync(ct);
        if (pull)
        {
            // Rules that look across the whole set (one voice per account, duplicates) run once everything is in.
            TrustPolicy.ApplyCrossPostRules(posts, now);
        }

        var meter = SocialScoreCalculator.Compute(posts, now, _options.Scoring);
        snapshot = await PersistAsync(snapshot, meter, mediaType, tmdbId, pull, attempt.Sources, now, ct);
        if (forceRefresh || pull)
            await LogRefreshAsync(title, trigger, pull, attempt.Outcome, attempt.Sources, now, ct);

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
            Sources = attempt.Sources,
            // A title the sources were never read for has no "last refreshed" time: inventing one ("updated just now") made a
            // paused or skipped read look like a finished one that found nothing.
            LastRefreshedAt = snapshot?.LastRefreshedAt,
            ReadsPaused = paused,
            FromCache = !pull,
            NextRefreshAllowedAt = paused
                ? LivePullBudget.RetryAfter(attempt.Refusal, now)
                : snapshot?.LastRefreshedAt.AddMinutes(_options.MinForcedRefreshMinutes)
        };
    }

    /// <summary>What happened when a reading was asked for: whether the sources were read, how each one answered, the word
    /// for the refresh log, and which guardrail (if any) turned a due pull away.</summary>
    private sealed record PullAttempt(bool Pulled, List<SourceStatusDto> Sources, string Outcome, PullRefusal Refusal);

    private async Task<PullAttempt> DecideAndPullAsync(Title title, TitleSnapshot? snapshot, bool tracked, bool forceRefresh, DateTime now, CancellationToken ct)
    {
        var sinceRefresh = snapshot is null ? TimeSpan.MaxValue : now - snapshot.LastRefreshedAt;
        var stale = sinceRefresh > TimeSpan.FromHours(_options.RefreshAfterHours);
        var recentlyPulled = sinceRefresh < TimeSpan.FromMinutes(_options.MinForcedRefreshMinutes);
        var decision = PullPolicy.Decide(tracked, stale, forceRefresh, recentlyPulled);

        if (decision != PullDecision.Pull)
            return new PullAttempt(false, Idle(null), PullPolicy.OutcomeName(decision, PullRefusal.None), PullRefusal.None);

        // The guardrails are the last line against a runaway bill; the reader sees the cache and a plain reason.
        // GuardrailLog has already written the trip (with the numbers) for the alert email.
        var refusal = _budget.TryAcquirePull();
        if (refusal != PullRefusal.None)
            return new PullAttempt(false, Idle(PublicNote(refusal)), PullPolicy.OutcomeName(decision, refusal), refusal);

        var status = await _ingestion.IngestAllAsync(_sources, title, now, ct);
        return new PullAttempt(true, status, PullPolicy.OutcomeName(decision, PullRefusal.None), PullRefusal.None);
    }

    /// <summary>What readers are told. Spend and caps stay out of public responses; the admin page has the numbers.</summary>
    private static string PublicNote(PullRefusal refusal) => refusal switch
    {
        PullRefusal.HourlyCap => "Live reads paused: hourly budget reached",
        PullRefusal.DailyCap or PullRefusal.SpendLimit => "Live reads paused for today",
        _ => "Live reads paused"
    };

    /// <summary>
    /// Notes a Refresh press or a pull in RefreshLogs: title, time, trigger and outcome, never who asked. Only requests
    /// that already used the database get here (per-visitor refusals are turned away earlier, in the controller), so
    /// logging adds one small row to work already under way and never wakes a paused database. A failed write is logged
    /// and ignored: the reading the reader asked for matters more than the record of it.
    /// </summary>
    private async Task LogRefreshAsync(Title title, ReadTrigger trigger, bool pulled, string outcome, List<SourceStatusDto> status, DateTime now, CancellationToken ct)
    {
        var entry = _db.RefreshLogs.Add(new RefreshLog
        {
            At = now,
            MediaType = title.MediaType,
            TmdbId = title.TmdbId,
            TitleName = title.Name.Clip(200),
            Trigger = trigger.ToString().ToLowerInvariant(),
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
