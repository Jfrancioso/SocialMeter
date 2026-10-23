using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// Process-wide ceiling on live pulls, the only thing in the system that costs money per call. The tracked set bounds
/// which titles can be pulled; this bounds how many pulls happen per hour regardless of how many readers ask, and how
/// many forced refreshes any one caller gets. Both are token buckets that refill continuously.
/// </summary>
public sealed class LivePullBudget : IDisposable
{
    /// <summary>How often the hourly budget hands back a pull; a turned-away reader can try again after this.</summary>
    public static readonly TimeSpan RefillEvery = TimeSpan.FromMinutes(5);

    private readonly TokenBucketRateLimiter _global;
    private readonly PartitionedRateLimiter<string> _perCaller;

    public LivePullBudget(IOptions<SocialMeterOptions> options)
    {
        var o = options.Value;
        var perHour = Math.Max(1, o.MaxLivePullsPerHour);
        _global = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perHour,
            TokensPerPeriod = Math.Max(1, perHour / 12),
            ReplenishmentPeriod = RefillEvery,
            QueueLimit = 0,
            AutoReplenishment = true
        });

        var perCaller = Math.Max(1, o.MaxForcedRefreshesPerHourPerCaller);
        _perCaller = PartitionedRateLimiter.Create<string, string>(caller => RateLimitPartition.GetTokenBucketLimiter(caller, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = perCaller,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMinutes(60.0 / perCaller),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    }

    /// <summary>Take one live pull from the hourly budget. False means serve the cache and say why.</summary>
    public bool TryAcquirePull()
    {
        using var lease = _global.AttemptAcquire();
        return lease.IsAcquired;
    }

    /// <summary>Take one forced refresh from this caller's allowance.</summary>
    public bool TryAcquireForcedRefresh(string caller)
    {
        using var lease = _perCaller.AttemptAcquire(caller);
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        _global.Dispose();
        _perCaller.Dispose();
    }
}
