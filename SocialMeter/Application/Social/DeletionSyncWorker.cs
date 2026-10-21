using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>Runs <see cref="DeletionSyncService"/> on a schedule for as long as the API is up.</summary>
public sealed class DeletionSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<DeletionSyncWorker> _logger;

    public DeletionSyncWorker(IServiceScopeFactory scopes, IOptions<SocialMeterOptions> options, ILogger<DeletionSyncWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.DeletionSyncHours));

        // Give the app a minute to settle after start-up before the first pass.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DeletionSyncService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deletion sync run failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
