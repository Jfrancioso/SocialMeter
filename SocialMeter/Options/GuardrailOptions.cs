namespace SocialMeter.Options;

/// <summary>
/// Every dial that keeps the bill bounded and the site honest when something upstream breaks, in one place. Each can be
/// changed as an App Service setting (for example Guardrails__LivePullsEnabled = false) without a deploy; the app
/// restarts and reads it. docs/OPERATIONS.md says what each one does when it trips and how you hear about it.
/// </summary>
public sealed class GuardrailOptions
{
    public const string SectionName = "Guardrails";

    /// <summary>The kill switch. False stops every live pull: readers get the cached reading and a "paused" note.</summary>
    public bool LivePullsEnabled { get; set; } = true;

    /// <summary>Live pulls per hour across the whole site. A token bucket: it hands one back every few minutes.</summary>
    public int MaxLivePullsPerHour { get; set; } = 12;

    /// <summary>Live pulls per UTC day. A plain count that holds even if the price estimate below is wrong.</summary>
    public int MaxLivePullsPerDay { get; set; } = 200;

    /// <summary>Forced refreshes (the Refresh button) one visitor may trigger per hour, by client address.</summary>
    public int MaxForcedRefreshesPerHourPerCaller { get; set; } = 6;

    /// <summary>
    /// Estimated paid spend (Claude + X) per UTC day at which live pulls stop until midnight UTC ("safe mode"). The estimate
    /// is counted tokens and posts times the prices below; the real bills are in the Anthropic and X consoles.
    /// </summary>
    public decimal DailySpendLimitUsd { get; set; } = 10m;

    // ---- Prices for the estimate. Claude Opus 5.5 is $4 / $20 per million tokens; X pay-per-use bills each item returned. ----

    public decimal ClaudeInputUsdPerMillion { get; set; } = 4m;
    public decimal ClaudeOutputUsdPerMillion { get; set; } = 20m;
    public decimal XUsdPerPost { get; set; } = 0.005m;
    public decimal XUsdPerUser { get; set; } = 0.01m;

    // ---- Circuit breakers, one per outside service (see SourceBreakers). ----

    /// <summary>Failures in a row after which a service (X, Bluesky, Steam, Claude) is skipped for a cool-down.</summary>
    public int BreakerFailures { get; set; } = 3;

    /// <summary>How long a tripped service is skipped before one trial call is let through.</summary>
    public int BreakerMinutes { get; set; } = 15;

    /// <summary>"Out of credit" (402), "slow down" (429) or a rejected key trip the breaker at once, for this long.</summary>
    public int BreakerHardStopMinutes { get; set; } = 60;
}
