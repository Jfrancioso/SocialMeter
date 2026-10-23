using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// Keeps our copies honest: anything a user deleted or a platform removed is purged here too, old posts are pruned,
/// and every affected title's score is recomputed. Runs on a timer (<see cref="DeletionSyncWorker"/>) and on demand.
/// </summary>
public sealed class DeletionSyncService
{
    private readonly SocialMeterDbContext _db;
    private readonly IEnumerable<ISocialSource> _sources;
    private readonly TitleRecomputer _recomputer;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<DeletionSyncService> _logger;

    public DeletionSyncService(
        SocialMeterDbContext db,
        IEnumerable<ISocialSource> sources,
        TitleRecomputer recomputer,
        IOptions<SocialMeterOptions> options,
        ILogger<DeletionSyncService> logger)
    {
        _db = db;
        _sources = sources;
        _recomputer = recomputer;
        _options = options.Value;
        _logger = logger;
    }

    // The worker and the operator endpoint must never run at once: both would load the same due rows and the second
    // SaveChanges would fail on rows the first already removed.
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public async Task<DeletionSyncResult> RunAsync(CancellationToken ct = default)
    {
        if (!await RunLock.WaitAsync(0, ct))
            throw new Common.OperationInProgressException("Deletion sync");
        try
        {
            return await RunCoreAsync(ct);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<DeletionSyncResult> RunCoreAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var result = new DeletionSyncResult { StartedAt = now };
        var affected = new HashSet<(MediaType, int)>();

        // 1. Retention: drop anything older than the window, whatever its state at the source.
        //    Steam reviews can't be re-checked individually, so they get the shorter window.
        var cutoff = now.AddDays(-_options.RetentionDays);
        var steamCutoff = now.AddDays(-_options.SteamReviewRetentionDays);
        var expired = await _db.SocialPosts
            .Where(p => p.CreatedAt < cutoff || (p.Platform == "steam" && p.CreatedAt < steamCutoff))
            .ToListAsync(ct);
        foreach (var p in expired) affected.Add((p.MediaType, p.TmdbId));
        _db.SocialPosts.RemoveRange(expired);
        result.Expired = expired.Count;

        // The refresh log keeps a short window: it records no visitor, but it has no reason to grow forever.
        var logCutoff = now.AddDays(-Math.Max(1, _options.RefreshLogRetentionDays));
        await _db.RefreshLogs.Where(r => r.At < logCutoff).ExecuteDeleteAsync(ct);

        // 2. Re-check posts that haven't been confirmed recently, per platform.
        var recheckBefore = now.AddHours(-_options.RecheckAfterHours);
        foreach (var source in _sources)
        {
            var status = new DeletionSyncSourceResult { Platform = source.Platform, Configured = source.IsConfigured };
            result.Sources.Add(status);
            if (!source.IsConfigured) continue;

            var due = await _db.SocialPosts
                .Where(p => p.Platform == source.Platform
                            && (p.LastCheckedAt == null || p.LastCheckedAt < recheckBefore))
                .OrderBy(p => p.LastCheckedAt ?? DateTime.MinValue)
                .Take(_options.DeletionSyncBatch)
                .ToListAsync(ct);

            if (due.Count == 0) continue;
            status.Checked = due.Count;

            IReadOnlySet<string> removed;
            try
            {
                removed = await source.FindRemovedAsync(due.Select(p => p.ExternalId).ToList(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Leave LastCheckedAt untouched so these come up first next run.
                _logger.LogWarning(ex, "{Platform} deletion check failed; will retry next run", source.Platform);
                status.Error = ex.Message;
                continue;
            }

            foreach (var post in due)
            {
                if (removed.Contains(post.ExternalId))
                {
                    _db.SocialPosts.Remove(post);
                    affected.Add((post.MediaType, post.TmdbId));
                    status.Removed++;
                }
                else
                {
                    post.LastCheckedAt = now;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        // 3. Recompute every title that lost posts so the meter never reflects content that is gone.
        foreach (var (mediaType, tmdbId) in affected)
            await _recomputer.RecomputeAsync(mediaType, tmdbId, now, ct);
        await _db.SaveChangesAsync(ct);

        result.TitlesRecomputed = affected.Count;
        result.FinishedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Deletion sync: {Checked} checked, {Removed} removed, {Expired} expired, {Titles} titles recomputed",
            result.Sources.Sum(s => s.Checked), result.Sources.Sum(s => s.Removed), result.Expired, result.TitlesRecomputed);

        return result;
    }
}

public sealed class DeletionSyncResult
{
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int Expired { get; set; }
    public int TitlesRecomputed { get; set; }
    public List<DeletionSyncSourceResult> Sources { get; } = new();
}

public sealed class DeletionSyncSourceResult
{
    public string Platform { get; set; } = "";
    public bool Configured { get; set; }
    public int Checked { get; set; }
    public int Removed { get; set; }
    public string? Error { get; set; }
}
