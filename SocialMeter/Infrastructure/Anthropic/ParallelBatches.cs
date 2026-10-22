namespace SocialMeter.Infrastructure.Anthropic;

/// <summary>
/// Splits items into fixed-size batches, runs a few batches at the same time, and returns the results in the original
/// order. The batches are exactly the ones a one-at-a-time loop would send, so only the waiting changes.
/// </summary>
public static class ParallelBatches
{
    public static async Task<IReadOnlyList<TOut>> RunAsync<TIn, TOut>(
        IReadOnlyList<TIn> items,
        int batchSize,
        int maxParallel,
        Func<IReadOnlyList<TIn>, Task<IReadOnlyList<TOut>>> runBatch,
        CancellationToken ct = default)
    {
        var results = new TOut[items.Count];
        if (items.Count == 0) return results;

        batchSize = Math.Max(1, batchSize);
        using var gate = new SemaphoreSlim(Math.Max(1, maxParallel));
        var work = new List<Task>();

        for (var start = 0; start < items.Count; start += batchSize)
        {
            var offset = start;
            var batch = items.Skip(start).Take(batchSize).ToList();
            work.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var output = await runBatch(batch);
                    if (output.Count != batch.Count)
                        throw new InvalidOperationException($"A batch of {batch.Count} returned {output.Count} results.");

                    // Each batch writes only its own slice, so no two tasks touch the same element.
                    for (var i = 0; i < batch.Count; i++)
                        results[offset + i] = output[i];
                }
                finally
                {
                    gate.Release();
                }
            }, ct));
        }

        await Task.WhenAll(work);
        return results;
    }
}
