using System.Net.Http.Headers;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Infrastructure.Anthropic;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Infrastructure.Imdb;
using SocialMeter.Infrastructure.Links;
using SocialMeter.Infrastructure.Omdb;
using SocialMeter.Infrastructure.Reddit;
using SocialMeter.Infrastructure.Steam;
using SocialMeter.Infrastructure.Tmdb;
using SocialMeter.Infrastructure.X;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure;

/// <summary>The infrastructure layer: everything that talks to the outside world. Persistence, provider clients, social sources, the model.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // ---- Options ----
        services
            .AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.SectionName))
            .Validate(o => o.IsConfigured, "Tmdb:ReadAccessToken or Tmdb:ApiKey is required.")
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be a valid absolute URL.")
            .ValidateOnStart();
        services.Configure<RedditOptions>(configuration.GetSection(RedditOptions.SectionName));
        services.Configure<ArcticShiftOptions>(configuration.GetSection(ArcticShiftOptions.SectionName));
        services.Configure<XOptions>(configuration.GetSection(XOptions.SectionName));
        services.Configure<OmdbOptions>(configuration.GetSection(OmdbOptions.SectionName));
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<RatingsOptions>(configuration.GetSection(RatingsOptions.SectionName));

        // ---- Persistence ----
        services.AddDbContext<SocialMeterDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        services.AddMemoryCache();

        // ---- Title and ratings providers ----
        services.AddHttpClient<TmdbClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<TmdbOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
            // The read-access token rides in a header so the credential never appears in a URL or a log.
            if (opts.UseBearer)
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ReadAccessToken);
        });
        services.AddHttpClient<OmdbClient>();
        services.AddHttpClient<SteamClient>();
        services.AddHttpClient<RatingLinkResolver>();
        services.AddHttpClient<PublishedScoreReader>();

        // IMDb's daily ratings file, held in memory and refreshed once a day; fills in new releases OMDb hasn't caught up with.
        services.AddSingleton<ImdbRatingsDataset>();
        services.AddHttpClient(nameof(ImdbRatingsWorker));
        services.AddHostedService<ImdbRatingsWorker>();

        // ---- Social sources: resolved as a set; add a platform by registering another ISocialSource ----
        services.AddHttpClient<RedditSource>();
        services.AddHttpClient<ArcticShiftSource>();
        services.AddHttpClient<XSource>();
        services.AddHttpClient<SteamReviewSource>();

        // Reddit: the official API in every environment. In Development only, ArcticShift:Enabled swaps in the archive for
        // local evaluation while the official application is pending; it can never be switched on in production.
        var useArchive = environment.IsDevelopment() && configuration.GetValue<bool>("ArcticShift:Enabled");
        services.AddScoped<ISocialSource>(sp => useArchive ? sp.GetRequiredService<ArcticShiftSource>() : sp.GetRequiredService<RedditSource>());
        services.AddScoped<ISocialSource>(sp => sp.GetRequiredService<XSource>());
        services.AddScoped<ISocialSource>(sp => sp.GetRequiredService<SteamReviewSource>());

        // ---- Post assessment ----
        // Claude reads posts in context (is it about this title? how does the author feel about *it*?) when an API key is
        // configured; otherwise the word lexicon scores tone on its own.
        services.AddSingleton<LexiconPostAssessorFallback>();
        services.AddSingleton(sp => new AnthropicClient { ApiKey = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey });
        services.AddSingleton<IPostAssessor>(sp =>
            sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.IsConfigured
                ? ActivatorUtilities.CreateInstance<ClaudePostAssessor>(sp)
                : sp.GetRequiredService<LexiconPostAssessorFallback>().Inner);

        return services;
    }
}
