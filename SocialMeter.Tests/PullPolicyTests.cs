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
    [InlineData(PullDecision.Pull, false, "pulled")]
    [InlineData(PullDecision.Pull, true, "budget-reached")]
    [InlineData(PullDecision.CoolingDown, false, "cooldown")]
    [InlineData(PullDecision.NotTracked, false, "not-tracked")]
    public void Names_the_outcome_stored_in_the_refresh_log(PullDecision decision, bool budgetRefused, string expected)
        => Assert.Equal(expected, PullPolicy.OutcomeName(decision, budgetRefused));

    [Fact]
    public void Outcome_names_fit_the_column()
    {
        foreach (var d in Enum.GetValues<PullDecision>())
            foreach (var refused in new[] { false, true })
                Assert.True(PullPolicy.OutcomeName(d, refused).Length <= 20);
    }
}
