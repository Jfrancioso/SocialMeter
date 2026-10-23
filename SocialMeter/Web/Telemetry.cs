using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;

namespace SocialMeter.Web;

/// <summary>
/// Server-side telemetry to Application Insights: every request (path, status, duration), every call we make to an outside
/// service, every log line at Information and above, and exceptions with their stack traces. It is how alerts see errors
/// and guardrail trips, and where to look when something breaks (docs/OPERATIONS.md).
///
/// Switched on only when the App Service setting APPLICATIONINSIGHTS_CONNECTION_STRING is present, so local runs and
/// tests send nothing. Nothing runs in visitors' browsers and no cookies are set; Application Insights drops the client
/// address after working out a rough location.
///
/// Every query-string value is replaced with "Redacted" before anything is sent, on our own requests (search terms) and on
/// outgoing calls (TMDB's api_key and OMDb's apikey travel in the URL). This is done here, explicitly, because the
/// instrumentation's own default did not do it: on 2026-10-23 the keys showed up in the first hour of telemetry.
/// </summary>
public static class Telemetry
{
    public const string ConnectionStringSetting = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string RedactedValue = "Redacted";

    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringSetting]))
            return services;

        services.AddOpenTelemetry().UseAzureMonitor();

        services.Configure<AspNetCoreTraceInstrumentationOptions>(o =>
        {
            // The uptime checks hit /api/health every few minutes; recording each one would only add noise and cost.
            o.Filter = context => !context.Request.Path.StartsWithSegments("/api/health");
            o.EnrichWithHttpRequest = (activity, request) =>
            {
                // Application Insights joins the path and this tag as they are, so the tag keeps its leading "?".
                if (request.QueryString.HasValue)
                    activity.SetTag("url.query", "?" + RedactQuery(request.QueryString.Value));
            };
        });

        services.Configure<HttpClientTraceInstrumentationOptions>(o =>
            o.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                if (request.RequestUri is { } uri)
                    activity.SetTag("url.full", RedactUrl(uri));
            });

        return services;
    }

    /// <summary>"?api_key=abc&amp;page=2" → "api_key=Redacted&amp;page=Redacted". Names stay, so a URL still says what it asked for.</summary>
    public static string RedactQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) return "";
        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)[0] + "=" + RedactedValue);
        return string.Join('&', parts);
    }

    /// <summary>The URL without user info, fragment or query values.</summary>
    public static string RedactUrl(Uri uri)
    {
        if (!uri.IsAbsoluteUri) return uri.OriginalString.Split('?', 2)[0];
        var bare = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return string.IsNullOrEmpty(uri.Query) ? bare : bare + "?" + RedactQuery(uri.Query);
    }
}
