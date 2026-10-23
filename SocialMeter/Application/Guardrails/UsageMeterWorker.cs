using Microsoft.EntityFrameworkCore;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Guardrails;

/// <summary>
/// Keeps UsageMeter and the UsageDays table in step: loads today's row before the app takes requests, saves changes every
/// minute, and saves once more on shutdown (deploys stop the app gracefully). A crash loses at most a minute of counts.
/// </summary>
public sealed class UsageMeterWorker : BackgroundService
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly UsageMeter _meter;
    private readonly ILogger<UsageMeterWorker> _logger;

    public UsageMeterWorker(IServiceScopeFactory scopes, UsageMeter meter, ILogger<UsageMeterWorker> logger)
    {
        _scopes = scopes;
        _meter = meter;
        _logger = logger;
    }

    /// <summary>Runs before the web server starts listening, so the first pull after a restart already sees today's spend.</summary>
    public override async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SocialMeterDbContext>();
            var day = _meter.Current.Day;
            var saved = await db.UsageDays.AsNoTracking().SingleOrDefaultAsync(d => d.Day == day, ct);
            if (saved is not null)
                _meter.Restore(new UsageTotals(saved.Day, saved.LivePulls, saved.ClaudeInputTokens, saved.ClaudeOutputTokens, saved.XPostsRead, saved.XUsersRead));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Starting with today's count at zero is a looser guard, not a broken site; say so loudly and carry on.
            _logger.LogError(ex, "Could not load today's usage; the spend guard starts from zero until the next restart");
        }

        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SaveEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SaveAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; StopAsync saves the last numbers.
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        await SaveAsync(ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var unsaved = _meter.TakeUnsaved();
        if (unsaved.Count == 0) return;

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SocialMeterDbContext>();
            foreach (var t in unsaved)
            {
                var row = await db.UsageDays.SingleOrDefaultAsync(d => d.Day == t.Day, ct);
                if (row is null) db.UsageDays.Add(row = new UsageDay { Day = t.Day });

                // The meter holds the day's full totals (it loaded the saved row at startup), so these overwrite.
                row.LivePulls = t.LivePulls;
                row.ClaudeInputTokens = t.ClaudeInputTokens;
                row.ClaudeOutputTokens = t.ClaudeOutputTokens;
                row.XPostsRead = t.XPostsRead;
                row.XUsersRead = t.XUsersRead;
                row.EstimatedUsd = Math.Round(_meter.EstimatedUsd(t), 4);
                row.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not save usage; will retry next minute");
            // Put the numbers back so the next save includes them.
            foreach (var t in unsaved) _meter.MarkUnsaved(t);
        }
    }
}
