using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using Xunit;

namespace SocialMeter.Tests;

public class PullGateTests
{
    [Fact]
    public async Task TryAcquire_skips_while_the_title_is_busy_and_succeeds_once_it_is_free()
    {
        var gate = new PullGate();
        var held = await gate.AcquireAsync(MediaType.Movie, 1, CancellationToken.None);

        Assert.Null(gate.TryAcquire(MediaType.Movie, 1));          // a pull is under way: skip, don't wait
        using (var other = gate.TryAcquire(MediaType.Movie, 2))     // another title is unaffected
            Assert.NotNull(other);

        held.Dispose();
        using var again = gate.TryAcquire(MediaType.Movie, 1);
        Assert.NotNull(again);
        Assert.Null(gate.TryAcquire(MediaType.Movie, 1));           // and it is exclusive in turn
    }
}
