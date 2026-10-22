using System.Diagnostics;
using SocialMeter.Infrastructure.Anthropic;
using Xunit;

namespace SocialMeter.Tests;

public class ParallelBatchesTests
{
    [Fact]
    public async Task Results_come_back_in_input_order_even_when_later_batches_finish_first()
    {
        var items = Enumerable.Range(0, 47).ToList();

        // The first batch is the slowest, so a naive "append as they finish" would scramble the order.
        var results = await ParallelBatches.RunAsync<int, string>(items, 20, 4, async batch =>
        {
            await Task.Delay(batch[0] == 0 ? 150 : 10);
            return batch.Select(i => $"#{i}").ToList();
        });

        Assert.Equal(items.Select(i => $"#{i}"), results);
    }

    [Fact]
    public async Task Sends_exactly_the_batches_a_sequential_loop_would()
    {
        var sizes = new System.Collections.Concurrent.ConcurrentBag<int>();
        await ParallelBatches.RunAsync<int, int>(Enumerable.Range(0, 60).ToList(), 20, 4, async batch =>
        {
            sizes.Add(batch.Count);
            await Task.Yield();
            return batch;
        });

        Assert.Equal(new[] { 20, 20, 20 }, sizes.OrderBy(s => s));
    }

    [Fact]
    public async Task Runs_batches_at_the_same_time_but_never_more_than_the_cap()
    {
        var running = 0;
        var peak = 0;
        var clock = Stopwatch.StartNew();

        await ParallelBatches.RunAsync<int, int>(Enumerable.Range(0, 120).ToList(), 20, 3, async batch =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(200);
            Interlocked.Decrement(ref running);
            return batch;
        });

        // Six batches of 200 ms, three at a time: about 400 ms instead of 1,200 ms one by one.
        Assert.Equal(3, peak);
        Assert.InRange(clock.ElapsedMilliseconds, 350, 1000);
    }

    [Fact]
    public async Task A_batch_that_returns_the_wrong_number_of_results_fails_loudly()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParallelBatches.RunAsync<int, int>(Enumerable.Range(0, 5).ToList(), 5, 2,
                batch => Task.FromResult<IReadOnlyList<int>>(batch.Take(4).ToList())));

    [Fact]
    public async Task Nothing_to_do_returns_nothing()
        => Assert.Empty(await ParallelBatches.RunAsync<int, int>(Array.Empty<int>(), 20, 4,
            _ => throw new InvalidOperationException("should not run")));

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
