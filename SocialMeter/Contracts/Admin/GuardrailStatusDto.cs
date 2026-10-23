using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Admin;

/// <summary>The admin page's "Health and spend" panel: today's usage against the limits, each outside service, watch windows.</summary>
public sealed class GuardrailStatusDto
{
    /// <summary>False when the kill switch is on (Guardrails:LivePullsEnabled = false).</summary>
    public bool LivePullsEnabled { get; init; }

    public UsageDayDto Today { get; init; } = null!;

    /// <summary>The six days before today, newest first; days with no usage are missing.</summary>
    public IReadOnlyList<UsageDayDto> PastDays { get; init; } = Array.Empty<UsageDayDto>();

    public decimal DailySpendLimitUsd { get; init; }
    public int MaxLivePullsPerDay { get; init; }
    public int MaxLivePullsPerHour { get; init; }

    public IReadOnlyList<ServiceHealthDto> Services { get; init; } = Array.Empty<ServiceHealthDto>();
    public IReadOnlyList<WatchWindowDto> WatchWindows { get; init; } = Array.Empty<WatchWindowDto>();
}

/// <summary>One UTC day of paid usage. EstimatedUsd is counted tokens and posts times the configured prices.</summary>
public sealed record UsageDayDto(DateOnly Day, int LivePulls, long ClaudeInputTokens, long ClaudeOutputTokens, int XPostsRead, int XUsersRead, decimal EstimatedUsd);

/// <summary>An outside service and its circuit breaker. Paused means calls are being skipped until PausedUntil.</summary>
public sealed record ServiceHealthDto(string Service, bool Paused, DateTimeOffset? PausedUntil, int FailuresInARow, string? LastError, DateTimeOffset? LastErrorAt);

public sealed record WatchWindowDto(string Label, MediaType MediaType, int TmdbId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int EveryMinutes, bool Open);
