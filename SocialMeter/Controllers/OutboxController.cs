using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Outbox;
using SocialMeter.Contracts.Admin;
using SocialMeter.Web.Filters;

namespace SocialMeter.Controllers;

/// <summary>
/// The admin page's Outbox: the brand account's drafted posts, and the Post and Skip taps. Admins only, behind the same
/// Cloudflare Access sign-in and checks as the rest of /api/admin; a change must come from the site (RequireAdmin).
/// </summary>
[ApiController]
[Route("api/admin/outbox")]
[RequireAdmin]
[EnableCors(Web.DependencyInjection.AdminCorsPolicy)]
public sealed class OutboxController : ControllerBase
{
    private readonly OutboxService _outbox;
    private readonly TimeProvider _clock;

    public OutboxController(OutboxService outbox, TimeProvider clock)
    {
        _outbox = outbox;
        _clock = clock;
    }

    private string Actor => HttpContext.Items[RequireAdminAttribute.ActorItem] as string ?? RequireAdminAttribute.KeyActor;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [HttpGet]
    public Task<OutboxDto> Get(CancellationToken ct) => _outbox.ListAsync(Now, ct);

    /// <summary>Makes today's draft now instead of waiting for the draft hour (it still skips titles posted lately).</summary>
    [HttpPost("draft")]
    public async Task<ActionResult<OutboxDto>> DraftNow(CancellationToken ct)
    {
        await _outbox.DraftNowAsync(Now, ct);
        return await _outbox.ListAsync(Now, ct);
    }

    [HttpPost("{id:int}/post")]
    public async Task<ActionResult<OutboxActionResultDto>> Post(int id, CancellationToken ct) =>
        await _outbox.PostAsync(id, Actor, Now, ct) is { } result ? result : NotFound();

    [HttpPost("{id:int}/skip")]
    public async Task<ActionResult<OutboxActionResultDto>> Skip(int id, CancellationToken ct) =>
        await _outbox.SkipAsync(id, Actor, Now, ct) is { } result ? result : NotFound();
}
