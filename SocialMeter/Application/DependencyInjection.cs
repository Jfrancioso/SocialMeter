using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Events;
using SocialMeter.Application.Guardrails;
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
        services.Configure<GuardrailOptions>(configuration.GetSection(GuardrailOptions.SectionName));
        services.Configure<WatchOptions>(configuration.GetSection(WatchOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

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
        services.AddScoped<GuardrailStatusService>();

        // Policies and guards: stateless or process-wide, so singletons.
        services.AddSingleton<TrustPolicy>();
        services.AddSingleton<VerdictAssessor>();
        services.AddSingleton<ISentimentAnalyzer, LexiconSentimentAnalyzer>();
        services.AddSingleton<PullGate>();

        // Guardrails (docs/OPERATIONS.md): what bounds the bill and what gets logged for the alert emails.
        services.AddSingleton<GuardrailLog>();
        services.AddSingleton<UsageMeter>();
        services.AddSingleton<SourceBreakers>();
        services.AddSingleton<LivePullBudget>();
        services.AddSingleton<WatchWindows>();

        // Background work: the tracked set is rebuilt on a schedule; stored posts are re-checked against their platforms;
        // critics' scores are refreshed; today's usage is saved every minute; watch windows are read on their schedule.
        services.AddHostedService<TrackedTitlesWorker>();
        services.AddHostedService<DeletionSyncWorker>();
        services.AddHostedService<Ratings.CriticsRefreshWorker>();
        services.AddHostedService<UsageMeterWorker>();
        services.AddHostedService<WatchWindowWorker>();

        return services;
    }
}
