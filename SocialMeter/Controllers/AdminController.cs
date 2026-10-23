using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Moderation;
using SocialMeter.Contracts.Admin;
using SocialMeter.Web.Filters;

namespace SocialMeter.Controllers;

/// <summary>
/// What the site's /admin page calls: who am I, hide or restore a post, and the moderation log. In production Cloudflare
/// Access stands in front of this path and the API checks its token again (see <see cref="RequireAdminAttribute"/>).
/// The only CORS policy that sends cookies is this controller's, so the Access cookie never rides along anywhere else.
/// </summary>
[ApiController]
[Route("api/admin")]
[RequireAdmin]
[EnableCors(Web.DependencyInjection.AdminCorsPolicy)]
public sealed class AdminController : ControllerBase
{
    private readonly ModerationService _moderation;
    private readonly IConfiguration _configuration;

    public AdminController(ModerationService moderation, IConfiguration configuration)
    {
        _moderation = moderation;
        _configuration = configuration;
    }

    private string Actor => HttpContext.Items[RequireAdminAttribute.ActorItem] as string ?? RequireAdminAttribute.KeyActor;

    [HttpGet("me")]
    public ActionResult<AdminMeDto> Me() => new AdminMeDto { Actor = Actor };

    /// <summary>
    /// The page sends the operator here to sign in: Cloudflare Access intercepts the request, asks for the one-time code,
    /// then lets it through with its cookie set for this host, and we send them back to the admin page. Only addresses on
    /// the site's own origins under /admin are accepted, so this can never be used to bounce someone to another site.
    /// </summary>
    [HttpGet("sign-in")]
    public IActionResult SignIn([FromQuery] string? returnTo) =>
        IsSafeReturn(returnTo, Web.DependencyInjection.AllowedOrigins(_configuration))
            ? Redirect(returnTo!)
            : BadRequest(new ProblemDetails { Status = 400, Title = "returnTo must be the site's /admin page" });

    /// <summary>An absolute https (or localhost) address on one of the site's origins whose path is /admin or below.</summary>
    public static bool IsSafeReturn(string? returnTo, IEnumerable<string> origins)
    {
        if (string.IsNullOrWhiteSpace(returnTo) || !Uri.TryCreate(returnTo, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        var origin = uri.GetLeftPart(UriPartial.Authority);
        var onSite = origins.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
        var path = uri.AbsolutePath;
        return onSite && (path == "/admin" || path.StartsWith("/admin/", StringComparison.Ordinal));
    }

    [HttpPost("posts/{id:int}/hide")]
    public async Task<ActionResult<ModerationResultDto>> Hide(int id, [FromBody] HidePostRequest request, CancellationToken ct)
    {
        if (!PostModeration.IsValidReason(request.Reason))
            return BadRequest(new ProblemDetails { Status = 400, Title = $"Reason must be one of: {string.Join(", ", PostModeration.Reasons)}" });

        var result = await _moderation.HideAsync(id, request.Reason, request.TitleName, Actor, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("posts/{id:int}/restore")]
    public async Task<ActionResult<ModerationResultDto>> Restore(int id, CancellationToken ct)
    {
        var result = await _moderation.RestoreAsync(id, Actor, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("moderation")]
    public async Task<ActionResult<IReadOnlyList<ModerationEntryDto>>> Moderation([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _moderation.RecentAsync(take, ct));
}
