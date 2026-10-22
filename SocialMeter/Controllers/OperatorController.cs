using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Common;
using SocialMeter.Application.Social;
using SocialMeter.Web.Filters;

namespace SocialMeter.Controllers;

/// <summary>
/// Operator actions that cost money or rewrite data. Every action needs the X-Admin-Key header (see AdminOptions) and
/// answers 409 when the same job is already running.
/// </summary>
[ApiController]
[Route("api/operator")]
[RequireAdminKey]
public sealed class OperatorController : ControllerBase
{
    private readonly IRescoreService _rescore;
    private readonly DeletionSyncService _deletionSync;
    private readonly ITitleTracker _tracker;

    public OperatorController(IRescoreService rescore, DeletionSyncService deletionSync, ITitleTracker tracker)
    {
        _rescore = rescore;
        _deletionSync = deletionSync;
        _tracker = tracker;
    }

    /// <summary>Re-assesses stored posts with the current assessor and trust rules. Posts already read are skipped unless ?force=true (costs tokens).</summary>
    [HttpPost("rescore")]
    public async Task<ActionResult> Rescore([FromQuery] bool force = false, CancellationToken ct = default)
    {
        try { return Ok(new { rescored = await _rescore.RescoreAsync(force, ct) }); }
        catch (OperationInProgressException ex) { return Conflict(new ProblemDetails { Status = 409, Title = ex.Message }); }
    }

    /// <summary>Re-checks stored posts at their platform and purges what was deleted or removed there, then recomputes affected scores.</summary>
    [HttpPost("sync-deletions")]
    public async Task<ActionResult<DeletionSyncResult>> SyncDeletions(CancellationToken ct)
    {
        try { return Ok(await _deletionSync.RunAsync(ct)); }
        catch (OperationInProgressException ex) { return Conflict(new ProblemDetails { Status = 409, Title = ex.Message }); }
    }

    /// <summary>Rebuilds the tracked set now from the current lists.</summary>
    [HttpPost("tracked/refresh")]
    public async Task<ActionResult<TrackedRefreshResult>> RefreshTracked(CancellationToken ct)
        => Ok(await _tracker.RefreshAsync(ct));
}
