using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Social;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/socialmeter")]
public sealed class SocialMeterController : ControllerBase
{
    private readonly ISocialMeterService _socialMeter;
    private readonly DeletionSyncService _deletionSync;

    public SocialMeterController(ISocialMeterService socialMeter, DeletionSyncService deletionSync)
    {
        _socialMeter = socialMeter;
        _deletionSync = deletionSync;
    }

    /// <summary>
    /// Runs the deletion sync now: stored posts are re-checked at their platform and any that were deleted or
    /// removed there are purged, then affected scores are recomputed. The background worker does this on a schedule.
    /// </summary>
    [HttpPost("sync-deletions")]
    public async Task<ActionResult<DeletionSyncResult>> SyncDeletions(CancellationToken ct)
        => Ok(await _deletionSync.RunAsync(ct));

    /// <summary>The SocialMeter reading for a title. Pass ?refresh=true to re-pull from the social sources now.</summary>
    [HttpGet("{type}/{id:int}")]
    public async Task<ActionResult<SocialMeterResponseDto>> Get([FromRoute] MediaType type, [FromRoute] int id, [FromQuery] bool refresh = false, CancellationToken ct = default)
    {
        var result = await _socialMeter.GetScoreAsync(type, id, refresh, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{type}/{id:int}/posts")]
    public async Task<ActionResult<PagedResult<SocialPostDto>>> Posts(
        [FromRoute] MediaType type,
        [FromRoute] int id,
        [FromQuery] Sentiment? sentiment = null,
        [FromQuery] string? platform = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _socialMeter.GetPostsAsync(type, id, sentiment, platform, page, pageSize, ct));

    /// <summary>Cached readings for many titles at once, e.g. ?keys=movie:693134,tv:1399. Never contacts the sources.</summary>
    [HttpGet("snapshots")]
    public async Task<ActionResult<IReadOnlyDictionary<string, SnapshotDto>>> Snapshots([FromQuery] string keys = "", CancellationToken ct = default)
    {
        var parsed = new List<(MediaType, int)>();
        foreach (var raw in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':');
            if (parts.Length == 2 && Enum.TryParse<MediaType>(parts[0], true, out var type) && int.TryParse(parts[1], out var id))
                parsed.Add((type, id));
        }

        if (parsed.Count > 200)
            return BadRequest(new { error = "At most 200 keys per request." });

        return Ok(await _socialMeter.GetSnapshotsAsync(parsed, ct));
    }
}
