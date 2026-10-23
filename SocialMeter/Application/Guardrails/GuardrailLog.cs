using System.Collections.Concurrent;

namespace SocialMeter.Application.Guardrails;

/// <summary>Names for the guardrails, as they appear in the log, the alert email and the admin page.</summary>
public static class Guardrail
{
    public const string KillSwitch = "kill-switch";
    public const string SpendLimit = "spend-limit";
    public const string DailyCap = "daily-cap";
    public const string HourlyCap = "hourly-cap";

    /// <summary>A circuit breaker opening for one service: "breaker:x", "breaker:claude"...</summary>
    public static string Breaker(string service) => "breaker:" + service;
}

/// <summary>
/// The one place guardrail trips are written to the log, always in the same shape: "Guardrail tripped: {Guardrail}. {Detail}".
/// The Azure alert rule searches Application Insights for that text and emails it (docs/OPERATIONS.md). Each guardrail is
/// logged at most once an hour, so an evening of refused pulls is one email, not hundreds.
/// </summary>
public sealed class GuardrailLog
{
    public static readonly EventId TrippedEvent = new(9001, "GuardrailTripped");
    public static readonly EventId RecoveredEvent = new(9002, "GuardrailRecovered");
    private static readonly TimeSpan Quiet = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastTripped = new();
    private readonly ILogger<GuardrailLog> _logger;
    private readonly TimeProvider _clock;

    public GuardrailLog(ILogger<GuardrailLog> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public void Tripped(string guardrail, string detail)
    {
        var now = _clock.GetUtcNow();
        if (_lastTripped.TryGetValue(guardrail, out var last) && now - last < Quiet) return;
        _lastTripped[guardrail] = now;

        // Hitting the hourly cap means lots of readers, which is good news, so it is logged but not emailed (the alert
        // rule leaves it out). Everything else is something to look at.
        var level = guardrail == Guardrail.HourlyCap ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(level, TrippedEvent, "Guardrail tripped: {Guardrail}. {Detail}", guardrail, OneLine(detail));
    }

    public void Recovered(string guardrail, string detail)
    {
        _lastTripped.TryRemove(guardrail, out _);
        _logger.LogInformation(RecoveredEvent, "Guardrail recovered: {Guardrail}. {Detail}", guardrail, OneLine(detail));
    }

    /// <summary>Error text from outside services goes into the log; one short line, so it can't forge extra log entries.</summary>
    private static string OneLine(string s)
    {
        var flat = s.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }
}
