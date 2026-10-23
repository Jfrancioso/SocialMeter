using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace SocialMeter.Web;

/// <summary>
/// Server-side telemetry to Application Insights: every request (path, status, duration), every call we make to an outside
/// service, every log line at Information and above, and exceptions with their stack traces. It is how alerts see errors
/// and guardrail trips, and where to look when something breaks (docs/OPERATIONS.md).
///
/// Switched on only when the App Service setting APPLICATIONINSIGHTS_CONNECTION_STRING is present, so local runs and
/// tests send nothing. Nothing runs in visitors' browsers and no cookies are set; Application Insights drops the client
/// address after working out a rough location, and query-string values (search terms, API keys in outgoing calls) are
/// redacted by the OpenTelemetry instrumentation.
/// </summary>
public static class Telemetry
{
    public const string ConnectionStringSetting = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringSetting]))
            return services;

        services.AddOpenTelemetry().UseAzureMonitor();

        // The uptime checks hit /api/health every few minutes; recording each one would only add noise and cost.
        services.Configure<AspNetCoreTraceInstrumentationOptions>(o =>
            o.Filter = context => !context.Request.Path.StartsWithSegments("/api/health"));

        return services;
    }
}
