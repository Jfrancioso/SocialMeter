using System.Collections.Concurrent;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>
/// One live pull per title at a time. Two tabs opening a stale title together must not each pay for a pull: the second
/// waits, then reads the snapshot the first wrote. Idle gates are pruned so the table cannot grow without bound under
/// id enumeration. Per process; behind two instances this becomes a database or cache lock.
/// </summary>
public sealed class PullGate
{
    private const int PruneAbove = 512;
    private readonly ConcurrentDictionary<(MediaType, int), SemaphoreSlim> _gates = new();

    public async Task<IDisposable> AcquireAsync(MediaType mediaType, int tmdbId, CancellationToken ct)
    {
        if (_gates.Count > PruneAbove)
            foreach (var (key, gate) in _gates)
                if (gate.CurrentCount == 1) _gates.TryRemove(key, out _);

        var semaphore = _gates.GetOrAdd((mediaType, tmdbId), _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    /// <summary>The gate if it is free right now, otherwise null. For work that can be skipped while a pull is under way.</summary>
    public IDisposable? TryAcquire(MediaType mediaType, int tmdbId)
    {
        var semaphore = _gates.GetOrAdd((mediaType, tmdbId), _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(0) ? new Releaser(semaphore) : null;
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
