using SocialMeter.Application.Abstractions;

namespace SocialMeter.Application.Tracking;

/// <summary>
/// Reads each open watch window's title on its schedule, as if someone pressed Refresh, so the reading keeps up with an
/// event (a watch party, a premiere) even when nobody has the page open. Every read goes through the normal path: the
/// per-title gate, the pull budget, the spend guard and the circuit breakers, and lands in RefreshLogs as "watch".
/// </summary>
public sealed class WatchWindowWorker : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly WatchWindows _windows;
    private readonly TimeProvider _clock;
    private readonly ILogger<WatchWindowWorker> _logger;

    // When each window's title was last read, by this process. A restart reads once straight away, which is fine:
    // the forced-refresh cool-down stops it if the title was read in the last few minutes.
    private readonly Dictionary<(Domain.Models.MediaType, int), DateTimeOffset> _lastRead = new();

    public WatchWindowWorker(IServiceScopeFactory scopes, WatchWindows windows, TimeProvider clock, ILogger<WatchWindowWorker> logger)
    {
        _scopes = scopes;
        _windows = windows;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery);
        try
        {
            do
            {
                foreach (var window in _windows.Open())
                    await ReadIfDueAsync(window, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task ReadIfDueAsync(Options.WatchWindow window, CancellationToken ct)
    {
        var key = (window.MediaType, window.TmdbId);
        var now = _clock.GetUtcNow();
        var every = TimeSpan.FromMinutes(Math.Max(WatchWindows.MinEveryMinutes, window.EveryMinutes));
        if (_lastRead.TryGetValue(key, out var last) && now - last < every) return;
        _lastRead[key] = now;

        try
        {
            using var scope = _scopes.CreateScope();
            var meter = scope.ServiceProvider.GetRequiredService<ISocialMeterService>();
            var reading = await meter.GetScoreAsync(window.MediaType, window.TmdbId, ReadTrigger.Watch, ct);
            _logger.LogInformation("Watch window '{Label}': read {Type} {Id}, score {Score} on {Posts} posts{Paused}",
                WatchWindows.ReasonFor(window), window.MediaType, window.TmdbId, reading?.Meter.Score, reading?.Meter.TotalPosts,
                reading?.ReadsPaused == true ? " (paused by a guardrail)" : "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Watch window read failed for {Type} {Id}", window.MediaType, window.TmdbId);
        }
    }
}
