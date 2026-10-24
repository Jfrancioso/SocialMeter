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
                    if (!IsFromOwnSite(http.Request.Method, http.Request.Headers.Origin.FirstOrDefault(),
                            SocialMeter.Web.DependencyInjection.AllowedOrigins(http.RequestServices.GetRequiredService<IConfiguration>())))
                    {
                        Log(http).LogWarning("Admin: a signed-in change from another site was refused");
                        context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Cross-site request refused" }) { StatusCode = 403 };
                        return;
                    }
                    http.Items[ActorItem] = email!;
                    return;
                }
                Log(http).LogWarning("Admin: a signed-in Cloudflare Access email is not on Admin:Emails");
                context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Not an admin" }) { StatusCode = 403 };
                return;
            }

            // A token that came with the request but failed the check: say which check (issuer, audience, signature,
            // expiry) by the exception type alone, so a misconfiguration is quick to spot and nothing from the token is logged.
            if (result.Failure is not null)
                Log(http).LogWarning("Admin: Cloudflare Access token rejected ({Check})", result.Failure.GetType().Name);
        }

        context.Result = new UnauthorizedObjectResult(new ProblemDetails { Status = 401, Title = "Sign in required" });
    }

    /// <summary>
    /// A change (any method but GET, HEAD or OPTIONS) made with the browser's sign-in must come from the site itself. The
    /// sign-in is Cloudflare's cookie, which a page on another site can make the browser send with a plain form post (a
    /// POST without a body needs no CORS preflight); the browser always names the page's site in Origin. Reads are safe:
    /// CORS stops another site from seeing the answer. Scripts using the admin key never reach this check.
    /// </summary>
    public static bool IsFromOwnSite(string method, string? origin, IEnumerable<string> allowedOrigins) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
        || (!string.IsNullOrEmpty(origin)
            && allowedOrigins.Any(o => string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)));

    private static ILogger Log(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<RequireAdminAttribute>();
}
