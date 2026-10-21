using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Options;

namespace SocialMeter.Application.Tracking;

/// <summary>Rebuilds the tracked set on a schedule so the first visitor after a quiet spell doesn't wait for it.</summary>
public sealed class TrackedTitlesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<TrackedTitlesWorker> _logger;

    public TrackedTitlesWorker(IServiceScopeFactory scopes, IOptions<SocialMeterOptions> options, ILogger<TrackedTitlesWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.TrackedRefreshHours));

        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ITitleTracker>().RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tracked-set refresh failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
