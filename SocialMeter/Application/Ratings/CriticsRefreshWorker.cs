using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Ratings;

/// <summary>
/// Refreshes the critics scores that are missing or stale (<see cref="CriticsScores.RefreshDueAsync"/>) on a schedule:
/// Ratings:CriticsRefreshBatch titles every Ratings:CriticsRefreshHours.
/// </summary>
public sealed class CriticsRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RatingsOptions _options;
    private readonly ILogger<CriticsRefreshWorker> _logger;

    public CriticsRefreshWorker(IServiceScopeFactory scopes, IOptions<RatingsOptions> options, ILogger<CriticsRefreshWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.CriticsRefreshHours));
        var batch = Math.Clamp(_options.CriticsRefreshBatch, 1, 200);

        // Give the app two minutes to settle after start-up before the first pass.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var done = await scope.ServiceProvider.GetRequiredService<CriticsScores>().RefreshDueAsync(batch, DateTime.UtcNow, stoppingToken);
                if (done > 0) _logger.LogInformation("Critics scores refreshed for {Count} titles", done);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critics refresh run failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
