using System.Net;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Guardrails;

/// <summary>Why a call to an outside service failed, and whether that alone should stop further calls.</summary>
/// <param name="Reason">Short and safe to show on the admin page: "out of credit (402)", "rate limited (429)", an exception type.</param>
/// <param name="HardStop">True for answers that won't get better by retrying soon: out of credit, slow down, key rejected.</param>
public readonly record struct BreakerFailure(string Reason, bool HardStop);

/// <summary>One service's breaker, for the admin page.</summary>
public sealed record BreakerStatus(string Service, bool Open, int FailuresInARow, DateTimeOffset? OpenUntil, string? LastError, DateTimeOffset? LastErrorAt);

/// <summary>
/// A circuit breaker for each outside service we pay for or can be rate-limited by: "x", "bluesky", "steam", "claude".
///
/// Closed (normal): calls go through. After BreakerFailures failures in a row, or one hard stop (out of credit, slow
/// down, key rejected), it opens: the service is skipped for a cool-down, readers get the other sources, and the trip is
/// logged for the alert email. When the cool-down ends the next call is a trial: success closes the breaker, another
/// failure opens it again. The point is to stop paying for (or hammering) a service that is answering with errors.
/// </summary>
public sealed class SourceBreakers
{
    private readonly object _lock = new();
    private readonly Dictionary<string, State> _states = new();
    private readonly GuardrailOptions _options;
    private readonly GuardrailLog _log;
    private readonly TimeProvider _clock;

    public SourceBreakers(IOptions<GuardrailOptions> options, GuardrailLog log, TimeProvider clock)
    {
        _options = options.Value;
        _log = log;
        _clock = clock;
    }

    /// <summary>False while the service's breaker is open: skip the call and treat the service as unavailable.</summary>
    public bool Allows(string service)
    {
        lock (_lock)
        {
            return !_states.TryGetValue(service, out var s) || s.OpenUntil is null || _clock.GetUtcNow() >= s.OpenUntil;
        }
    }

    public void Succeeded(string service)
    {
        bool wasOpen;
        lock (_lock)
        {
            if (!_states.TryGetValue(service, out var s)) return;
            wasOpen = s.OpenUntil is not null;
            s.FailuresInARow = 0;
            s.OpenUntil = null;
        }
        if (wasOpen) _log.Recovered(Guardrail.Breaker(service), $"{service} is answering again");
    }

    public void Failed(string service, Exception ex) => Failed(service, Classify(ex));

    public void Failed(string service, BreakerFailure failure)
    {
        DateTimeOffset? openedUntil = null;
        lock (_lock)
        {
            if (!_states.TryGetValue(service, out var s)) _states[service] = s = new State();
            var now = _clock.GetUtcNow();
            s.FailuresInARow++;
            s.LastError = failure.Reason;
            s.LastErrorAt = now;

            if (failure.HardStop)
                openedUntil = s.OpenUntil = now.AddMinutes(Math.Max(1, _options.BreakerHardStopMinutes));
            else if (s.FailuresInARow >= Math.Max(1, _options.BreakerFailures))
                openedUntil = s.OpenUntil = now.AddMinutes(Math.Max(1, _options.BreakerMinutes));
        }

        if (openedUntil is { } until)
            _log.Tripped(Guardrail.Breaker(service), $"{service} paused until {until:HH:mm} UTC: {failure.Reason}");
    }

    public IReadOnlyList<BreakerStatus> Snapshot()
    {
        lock (_lock)
        {
            var now = _clock.GetUtcNow();
            return _states
                .OrderBy(kv => kv.Key)
                .Select(kv => new BreakerStatus(kv.Key, kv.Value.OpenUntil > now, kv.Value.FailuresInARow, kv.Value.OpenUntil, kv.Value.LastError, kv.Value.LastErrorAt))
                .ToList();
        }
    }

    /// <summary>How a failed HTTP call is read. Services with their own client library (Claude) classify their own errors.</summary>
    public static BreakerFailure Classify(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.PaymentRequired } => new("out of credit (402)", HardStop: true),
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => new("rate limited (429)", HardStop: true),
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } h => new($"key rejected ({(int)h.StatusCode!})", HardStop: true),
        HttpRequestException { StatusCode: { } code } => new($"HTTP {(int)code}", HardStop: false),
        _ => new(ex.GetType().Name, HardStop: false)
    };

    private sealed class State
    {
        public int FailuresInARow;
        public DateTimeOffset? OpenUntil;
        public string? LastError;
        public DateTimeOffset? LastErrorAt;
    }
}
