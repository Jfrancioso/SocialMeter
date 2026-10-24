namespace SocialMeter.Application.Outbox;

/// <summary>
/// Makes the Outbox's drafts: every few minutes it asks whether a live event needs one or the daily draft is due (see
/// OutboxService.DraftDueAsync). It only drafts; posting is always an admin's tap. Reads stored data only.
/// </summary>
public sealed class OutboxWorker : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxWorker> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstCheckAfter, stoppingToken);   // let startup (migrations, warm-up) finish first
            using var timer = new PeriodicTimer(CheckEvery);
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<OutboxService>().DraftDueAsync(_clock.GetUtcNow().UtcDateTime, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Outbox: drafting failed; trying again in {Minutes} minutes", CheckEvery.TotalMinutes);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
