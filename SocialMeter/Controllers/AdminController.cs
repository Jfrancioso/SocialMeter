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
    /// then lets it through with its cookie set for this host, and we send them back to the admin page. The address we
    /// send them to is rebuilt from configuration (the matching site origin + /admin), never echoed from the request, so
    /// this can never be used to bounce someone to another site.
    /// </summary>
    [HttpGet("sign-in")]
    public IActionResult SignIn([FromQuery] string? returnTo) =>
        SafeReturnOrigin(returnTo, Web.DependencyInjection.AllowedOrigins(_configuration)) is { } origin
            ? Redirect($"{origin}/admin")
            : BadRequest(new ProblemDetails { Status = 400, Title = "returnTo must be the site's /admin page" });

    /// <summary>
    /// For an absolute https (or localhost) address on one of the site's origins whose path is /admin or below: that
    /// origin as configured. Otherwise null.
    /// </summary>
    public static string? SafeReturnOrigin(string? returnTo, IEnumerable<string> origins)
    {
        if (string.IsNullOrWhiteSpace(returnTo) || !Uri.TryCreate(returnTo, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;

        var path = uri.AbsolutePath;
        if (path != "/admin" && !path.StartsWith("/admin/", StringComparison.Ordinal)) return null;

        var origin = uri.GetLeftPart(UriPartial.Authority);
        return origins.Select(o => o.TrimEnd('/')).FirstOrDefault(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
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
