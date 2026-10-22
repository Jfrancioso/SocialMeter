namespace SocialMeter.Application.Social;

/// <summary>Why a live pull did or didn't run, before the hourly budget is consulted.</summary>
public enum PullDecision
{
    /// <summary>Contact the sources.</summary>
    Pull,

    /// <summary>The title is outside the tracked set, so it is never pulled.</summary>
    NotTracked,

    /// <summary>A reader asked, but the title was pulled within the cool-down.</summary>
    CoolingDown,

    /// <summary>Nobody asked and the cached reading is still fresh.</summary>
    Fresh
}

/// <summary>The rule that bounds the X bill, kept pure so it can be tested without a database.</summary>
public static class PullPolicy
{
    public static PullDecision Decide(bool tracked, bool stale, bool forceRefresh, bool recentlyPulled)
    {
        if (!tracked) return PullDecision.NotTracked;
        if (stale) return PullDecision.Pull;
        if (forceRefresh) return recentlyPulled ? PullDecision.CoolingDown : PullDecision.Pull;
        return PullDecision.Fresh;
    }

    /// <summary>The word stored in RefreshLog.Outcome.</summary>
    public static string OutcomeName(PullDecision decision, bool budgetRefused) => decision switch
    {
        PullDecision.Pull => budgetRefused ? "budget-reached" : "pulled",
        PullDecision.NotTracked => "not-tracked",
        PullDecision.CoolingDown => "cooldown",
        _ => "fresh"
    };
}
