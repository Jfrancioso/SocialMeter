using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Guardrails;

/// <summary>Why a live pull was turned away. None means it may go ahead.</summary>
public enum PullRefusal
{
    None,

    /// <summary>Guardrails:LivePullsEnabled is false.</summary>
    KillSwitch,

    /// <summary>Today's estimated spend reached Guardrails:DailySpendLimitUsd ("safe mode" until midnight UTC).</summary>
    SpendLimit,

    /// <summary>Today's pulls reached Guardrails:MaxLivePullsPerDay.</summary>
    DailyCap,

    /// <summary>The hourly bucket is empty; it hands one back every few minutes.</summary>
    HourlyCap
}

/// <summary>
/// The one door every live pull goes through, because pulls are what cost money (X per post, Claude per post read). The
/// tracked set bounds which titles can be pulled; this bounds how many and how much, checked cheapest-first:
/// the kill switch, today's estimated spend, today's pull count, then the hourly token bucket. A separate per-visitor
/// bucket limits how often one visitor can press Refresh.
/// </summary>
public sealed class LivePullBudget : IDisposable
{
    /// <summary>How often the hourly budget hands back a pull; a turned-away reader can try again after this.</summary>
    public static readonly TimeSpan RefillEvery = TimeSpan.FromMinutes(5);

    private readonly TokenBucketRateLimiter _hourly;
    private readonly PartitionedRateLimiter<string> _perCaller;
    private readonly GuardrailOptions _options;
    private readonly UsageMeter _usage;
    private readonly GuardrailLog _log;

    public LivePullBudget(IOptions<GuardrailOptions> options, UsageMeter usage, GuardrailLog log)
    {
        _options = options.Value;
        _usage = usage;
        _log = log;

        var perHour = Math.Max(1, _options.MaxLivePullsPerHour);
        _hourly = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perHour,
            TokensPerPeriod = Math.Max(1, perHour / 12),
            ReplenishmentPeriod = RefillEvery,
            QueueLimit = 0,
            AutoReplenishment = true
        });

        var perCaller = Math.Max(1, _options.MaxForcedRefreshesPerHourPerCaller);
        _perCaller = PartitionedRateLimiter.Create<string, string>(caller => RateLimitPartition.GetTokenBucketLimiter(caller, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = perCaller,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMinutes(60.0 / perCaller),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    }

    public int ForcedRefreshesPerHour => Math.Max(1, _options.MaxForcedRefreshesPerHourPerCaller);

    /// <summary>
    /// Take one live pull. None means go ahead (and it is counted); anything else means serve the cache and say why.
    /// Two pulls arriving together can both pass the daily checks, so the daily numbers can overshoot by one or two.
    /// </summary>
    public PullRefusal TryAcquirePull()
    {
        if (!_options.LivePullsEnabled)
            return Refuse(PullRefusal.KillSwitch, Guardrail.KillSwitch, "Live pulls are switched off (Guardrails:LivePullsEnabled = false)");

        var today = _usage.Current;
        var spent = _usage.EstimatedUsd(today);
        if (spent >= _options.DailySpendLimitUsd)
            return Refuse(PullRefusal.SpendLimit, Guardrail.SpendLimit,
                $"Estimated spend today is ${spent:0.00}, at the ${_options.DailySpendLimitUsd:0.00} limit; live pulls resume at midnight UTC");

        if (today.LivePulls >= _options.MaxLivePullsPerDay)
            return Refuse(PullRefusal.DailyCap, Guardrail.DailyCap,
                $"{today.LivePulls} live pulls today, at the cap of {_options.MaxLivePullsPerDay}; they resume at midnight UTC");

        using (var lease = _hourly.AttemptAcquire())
        {
            if (!lease.IsAcquired)
                return Refuse(PullRefusal.HourlyCap, Guardrail.HourlyCap, $"All {_options.MaxLivePullsPerHour} live pulls for this hour are used");
        }

        _usage.RecordLivePull();
        return PullRefusal.None;
    }

    /// <summary>Take one forced refresh from this caller's allowance.</summary>
    public bool TryAcquireForcedRefresh(string caller)
    {
        using var lease = _perCaller.AttemptAcquire(caller);
        return lease.IsAcquired;
    }

    /// <summary>When a turned-away reader may expect live reads again, if we know.</summary>
    public static DateTime? RetryAfter(PullRefusal refusal, DateTime now) => refusal switch
    {
        PullRefusal.HourlyCap => now + RefillEvery,
        PullRefusal.DailyCap or PullRefusal.SpendLimit => now.Date.AddDays(1),
        _ => null
    };

    private PullRefusal Refuse(PullRefusal refusal, string guardrail, string detail)
    {
        _log.Tripped(guardrail, detail);
        return refusal;
    }

    public void Dispose()
    {
        _hourly.Dispose();
        _perCaller.Dispose();
    }
}
