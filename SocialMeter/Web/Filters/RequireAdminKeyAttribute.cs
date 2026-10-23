using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Web.Filters;

/// <summary>
/// Guards operator endpoints with the shared key from <see cref="AdminOptions"/>. Fails closed: no key configured means
/// 503, wrong or missing header means 401. The comparison is constant-time so the key cannot be guessed byte by byte.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireAdminKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value;
        if (!options.IsConfigured)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 503, Title = "Operator endpoints are disabled", Detail = "Set Admin:ApiKey to enable them." })
            { StatusCode = 503 };
            return Task.CompletedTask;
        }

        if (!AdminKey.Matches(context.HttpContext.Request.Headers[AdminOptions.HeaderName].FirstOrDefault(), options))
            context.Result = new UnauthorizedObjectResult(new ProblemDetails { Status = 401, Title = "Admin key required" });

        return Task.CompletedTask;
    }
}
