using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using SocialMeter.Options;

namespace SocialMeter.Web;

/// <summary>The presentation layer: controllers, JSON shape, API docs, CORS, request-rate guards. No business logic.</summary>
public static class DependencyInjection
{
    public const string CorsPolicy = "Frontend";

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

        // Per-client ceiling on every API call, so id enumeration cannot burn the TMDB/OMDb quotas or the Anthropic bill.
        // Live-pull budgets are separate (LivePullBudget); this is the plain request-rate guard.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 4, QueueLimit = 0 }));
        });

        // Origins come from configuration (Cors:AllowedOrigins) so production is not hard-coded; no credentials until there is a session.
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" };
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

        return services;
    }

    /// <summary>The request pipeline, in order. Kept beside the registrations so the whole web layer reads in one place.</summary>
    public static WebApplication UseWeb(this WebApplication app)
    {
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
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }
}
