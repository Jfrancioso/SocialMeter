using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Events;
using SocialMeter.Application.Moderation;
using SocialMeter.Application.Ratings;
using SocialMeter.Application.Social;
using SocialMeter.Application.Titles;
using SocialMeter.Application.Tracking;
using SocialMeter.Options;

namespace SocialMeter.Application;

/// <summary>The application layer: use-case services, policies and the workers that drive them. No HTTP, no external clients.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SocialMeterOptions>(configuration.GetSection(SocialMeterOptions.SectionName));

        // Use cases, one class each.
        services.AddScoped<ISocialMeterService, SocialMeterService>();
        services.AddScoped<IScoreExplainer, ScoreExplainer>();
        services.AddScoped<IPostQueries, PostQueryService>();
        services.AddScoped<IRescoreService, RescoreService>();
        services.AddScoped<ITitleService, TitleService>();
        services.AddScoped<IRatingsService, RatingsService>();
        services.AddScoped<ITitleTracker, TitleTracker>();
        services.AddScoped<PostIngestionService>();
        services.AddScoped<ScoreHistoryService>();
        services.AddScoped<DeletionSyncService>();
        services.AddScoped<TitleRecomputer>();
        services.AddScoped<ModerationService>();
        services.AddScoped<OutboundClicks>();
        services.AddScoped<Ratings.CriticsScores>();

        // Policies and guards: stateless or process-wide, so singletons.
        services.AddSingleton<TrustPolicy>();
        services.AddSingleton<VerdictAssessor>();
        services.AddSingleton<ISentimentAnalyzer, LexiconSentimentAnalyzer>();
        services.AddSingleton<PullGate>();
        services.AddSingleton<LivePullBudget>();

        // Background work: the tracked set is rebuilt on a schedule; stored posts are re-checked against their platforms.
        services.AddHostedService<TrackedTitlesWorker>();
        services.AddHostedService<DeletionSyncWorker>();
        services.AddHostedService<Ratings.CriticsRefreshWorker>();

        return services;
    }
}
