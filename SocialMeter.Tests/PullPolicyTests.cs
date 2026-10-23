using SocialMeter.Application.Guardrails;
using SocialMeter.Application.Social;
using Xunit;

namespace SocialMeter.Tests;

public class PullPolicyTests
{
    [Theory]
    [InlineData(false, true, true, false, PullDecision.NotTracked)]   // untracked titles never pull, however stale or pressed
    [InlineData(true, true, false, false, PullDecision.Pull)]         // a page view finds a tracked title stale
    [InlineData(true, false, true, false, PullDecision.Pull)]         // Refresh pressed outside the cool-down
    [InlineData(true, false, true, true, PullDecision.CoolingDown)]   // Refresh pressed right after a pull
    [InlineData(true, false, false, false, PullDecision.Fresh)]       // ordinary page view of a fresh reading
    public void Decides_when_a_live_pull_runs(bool tracked, bool stale, bool force, bool recentlyPulled, PullDecision expected)
        => Assert.Equal(expected, PullPolicy.Decide(tracked, stale, force, recentlyPulled));

    [Theory]
    [InlineData(PullDecision.Pull, PullRefusal.None, "pulled")]
    [InlineData(PullDecision.Pull, PullRefusal.HourlyCap, "budget-reached")]
    [InlineData(PullDecision.Pull, PullRefusal.DailyCap, "daily-cap")]
    [InlineData(PullDecision.Pull, PullRefusal.SpendLimit, "spend-limit")]
    [InlineData(PullDecision.Pull, PullRefusal.KillSwitch, "switched-off")]
    [InlineData(PullDecision.CoolingDown, PullRefusal.None, "cooldown")]
    [InlineData(PullDecision.NotTracked, PullRefusal.None, "not-tracked")]
    public void Names_the_outcome_stored_in_the_refresh_log(PullDecision decision, PullRefusal refusal, string expected)
        => Assert.Equal(expected, PullPolicy.OutcomeName(decision, refusal));

    [Fact]
    public void Outcome_names_fit_the_column()
    {
        foreach (var d in Enum.GetValues<PullDecision>())
            foreach (var refusal in Enum.GetValues<PullRefusal>())
                Assert.True(PullPolicy.OutcomeName(d, refusal).Length <= 20);
    }
}
