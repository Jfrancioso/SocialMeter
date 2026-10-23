using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using SocialMeter.Options;

namespace SocialMeter.Web;

/// <summary>The presentation layer: controllers, JSON shape, API docs, CORS, request-rate guards. No business logic.</summary>
public static class DependencyInjection
{
    public const string CorsPolicy = "Frontend";

    /// <summary>Rate-limit policy for the endpoints that return bulk data (post feed, explanations, history, batch snapshots, search).</summary>
    public const string ReadsPolicy = "reads";

    public static IServiceCollection AddWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));

        services
            .AddControllers()
            .AddJsonOptions(o =>
            {
                // Enums go over the wire as "movie" / "tv" / "positive" rather than numbers.
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddProblemDetails();

        // Behind App Service the connection comes from Azure's front end; the visitor's address is in X-Forwarded-For.
        // ForwardLimit 1 reads only the entry the front end itself appended, so a client cannot spoof its way to a fresh bucket.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });

        // Per-client ceilings, a backstop rather than the main defence (see RateLimitOptions). They used to be 60 and 20 per
        // minute, which a single visitor clicking through a few titles could hit (a home page is ~5 requests, a title page
        // ~6), and which everyone behind one carrier or office address shared. Abuse is stopped by Cloudflare's edge rule;
        // money is guarded by LivePullBudget.
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(ClientKey(ctx),
                    _ => new SlidingWindowRateLimiterOptions { PermitLimit = Math.Max(1, limits.GlobalPerMinute), Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 4, QueueLimit = 0 }));
            options.AddPolicy(ReadsPolicy, ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(ClientKey(ctx),
                    _ => new SlidingWindowRateLimiterOptions { PermitLimit = Math.Max(1, limits.ReadsPerMinute), Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 4, QueueLimit = 0 }));
        });

        // Origins come from configuration (Cors:AllowedOrigins) so production is not hard-coded; no credentials until there is a session.
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" };
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

        return services;
    }

    /// <summary>The request pipeline, in order. Kept beside the registrations so the whole web layer reads in one place.</summary>
    public static WebApplication UseWeb(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseCors(CorsPolicy);
        app.UseRateLimiter();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            app.UseHsts();
        }

        app.UseExceptionHandler();
        app.UseHttpsRedirection();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            ctx.Response.Headers["Cache-Control"] = ctx.Response.Headers["Cache-Control"].Count > 0 ? ctx.Response.Headers["Cache-Control"] : "no-store";
            await next();
        });
        app.UseAuthorization();
        app.MapControllers();

        // The API is for the site, not for crawlers. Only polite bots honour this; the rate limits handle the rest.
        app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nDisallow: /\n", "text/plain"));
        return app;
    }

    private static string ClientKey(HttpContext ctx) => ClientAddress.For(ctx);
}
