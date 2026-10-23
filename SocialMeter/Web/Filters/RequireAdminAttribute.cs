using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SocialMeter.Options;
using SocialMeter.Web.Auth;

namespace SocialMeter.Web.Filters;

/// <summary>
/// Guards the admin endpoints the site's admin page calls. Two ways in: a valid Cloudflare Access token whose email is on
/// the Admin:Emails list (the browser, in production, where Cloudflare has already made the operator sign in), or the
/// X-Admin-Key header (scripts and local development). Fails closed: 503 when neither is configured, 401 without valid
/// credentials, 403 for a signed-in email that is not an admin. The caller is left in HttpContext.Items for the audit log.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string ActorItem = "admin.actor";
    public const string KeyActor = "admin key";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var admin = http.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value;
        var access = http.RequestServices.GetRequiredService<IOptions<CloudflareAccessOptions>>().Value;

        if (!admin.IsConfigured && !access.IsConfigured)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 503, Title = "Admin endpoints are disabled", Detail = "Configure CloudflareAccess or Admin:ApiKey." })
            { StatusCode = 503 };
            return;
        }

        if (AdminKey.Matches(http.Request.Headers[AdminOptions.HeaderName].FirstOrDefault(), admin))
        {
            http.Items[ActorItem] = KeyActor;
            return;
        }

        if (access.IsConfigured)
        {
            var result = await http.AuthenticateAsync(CloudflareAccess.Scheme);
            if (result.Succeeded)
            {
                var email = result.Principal?.FindFirst(CloudflareAccess.EmailClaim)?.Value;
                if (AdminKey.IsAdminEmail(email, admin))
                {
                    http.Items[ActorItem] = email!;
                    return;
                }
                context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Not an admin" }) { StatusCode = 403 };
                return;
            }
        }

        context.Result = new UnauthorizedObjectResult(new ProblemDetails { Status = 401, Title = "Sign in required" });
    }
}
