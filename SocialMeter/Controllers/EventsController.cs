using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Events;

namespace SocialMeter.Controllers;

/// <summary>
/// Small, anonymous counters the site sends as people use it. Today only outbound link clicks (see
/// <see cref="OutboundClicks"/>). Parameters ride in the query string so the browser sends a plain request with no
/// preflight; there is never a body, a cookie or an identifier.
/// </summary>
[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly OutboundClicks _clicks;

    public EventsController(OutboundClicks clicks) => _clicks = clicks;

    [HttpPost("outbound")]
    public async Task<IActionResult> Outbound([FromQuery] string? kind, [FromQuery] string? host, CancellationToken ct)
    {
        if (OutboundClicks.Normalize(kind, host) is { } click)
            await _clicks.RecordAsync(click.Kind, click.Host, DateTime.UtcNow, ct);
        return NoContent();   // the same answer either way: nothing to probe
    }
}
