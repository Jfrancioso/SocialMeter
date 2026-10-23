using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Tracking;
using SocialMeter.Contracts.Admin;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Guardrails;

/// <summary>Puts the guardrails' state together for the admin page. Reads only; changing a limit is an App Service setting.</summary>
public sealed class GuardrailStatusService
{
    private readonly SocialMeterDbContext _db;
    private readonly UsageMeter _usage;
    private readonly SourceBreakers _breakers;
    private readonly WatchWindows _windows;
    private readonly IEnumerable<ISocialSource> _sources;
    private readonly IPostAssessor _assessor;
    private readonly GuardrailOptions _options;

    public GuardrailStatusService(SocialMeterDbContext db, UsageMeter usage, SourceBreakers breakers, WatchWindows windows,
        IEnumerable<ISocialSource> sources, IPostAssessor assessor, IOptions<GuardrailOptions> options)
    {
        _db = db;
        _usage = usage;
        _breakers = breakers;
        _windows = windows;
        _sources = sources;
        _assessor = assessor;
        _options = options.Value;
    }

    public async Task<GuardrailStatusDto> GetAsync(CancellationToken ct = default)
    {
        var today = _usage.Current;
        var since = today.Day.AddDays(-6);
        var past = await _db.UsageDays.AsNoTracking()
            .Where(d => d.Day >= since && d.Day < today.Day)
            .OrderByDescending(d => d.Day)
            .Select(d => new UsageDayDto(d.Day, d.LivePulls, d.ClaudeInputTokens, d.ClaudeOutputTokens, d.XPostsRead, d.XUsersRead, d.EstimatedUsd))
            .ToListAsync(ct);

        // Every service we call, healthy or not; a breaker only has a record once its service has failed at least once.
        var states = _breakers.Snapshot().ToDictionary(b => b.Service);
        var services = _sources.Where(s => s.IsConfigured).Select(s => s.Platform)
            .Append(_assessor.Name)
            .Distinct()
            .Select(name => states.TryGetValue(name, out var b)
                ? new ServiceHealthDto(name, b.Open, b.Open ? b.OpenUntil : null, b.FailuresInARow, b.LastError, b.LastErrorAt)
                : new ServiceHealthDto(name, false, null, 0, null, null))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        return new GuardrailStatusDto
        {
            LivePullsEnabled = _options.LivePullsEnabled,
            Today = new UsageDayDto(today.Day, today.LivePulls, today.ClaudeInputTokens, today.ClaudeOutputTokens, today.XPostsRead, today.XUsersRead,
                Math.Round(_usage.EstimatedUsd(today), 2)),
            PastDays = past,
            DailySpendLimitUsd = _options.DailySpendLimitUsd,
            MaxLivePullsPerDay = _options.MaxLivePullsPerDay,
            MaxLivePullsPerHour = _options.MaxLivePullsPerHour,
            Services = services,
            WatchWindows = _windows.All
                .Where(w => w.EndsAt > now.AddDays(-1))
                .Select(w => new WatchWindowDto(WatchWindows.ReasonFor(w), w.MediaType, w.TmdbId, w.StartsAt, w.EndsAt, w.EveryMinutes, w.IsOpen(now)))
                .ToList()
        };
    }
}
