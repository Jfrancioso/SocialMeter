using SocialMeter.Domain.Models;

namespace SocialMeter.Options;

/// <summary>
/// Scheduled live reads for a title during a known event: a watch party, a premiere, a finale. While a window is open the
/// title counts as tracked and is read every EveryMinutes, through the same budget and guardrails as any other pull.
/// Set as App Service settings, times in UTC:
/// Watch__Windows__0__MediaType = movie, __TmdbId, __Label, __StartsAt = 2026-10-24T01:00:00Z, __EndsAt, __EveryMinutes.
/// </summary>
public sealed class WatchOptions
{
    public const string SectionName = "Watch";

    public List<WatchWindow> Windows { get; set; } = new();
}

public sealed class WatchWindow
{
    public MediaType MediaType { get; set; }
    public int TmdbId { get; set; }

    /// <summary>Shown on the title page's "Tracking ·" chip while the window is open, e.g. "Watch party tonight".</summary>
    public string Label { get; set; } = "";

    // DateTimeOffset rather than DateTime: "2026-10-24T01:00:00Z" then means the same instant on a laptop in New York
    // and on the server, where DateTime would be converted to the machine's local time.
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Minutes between reads. Never below the forced-refresh cool-down (15), which would refuse them anyway.</summary>
    public int EveryMinutes { get; set; } = 30;

    public bool IsOpen(DateTimeOffset now) => now >= StartsAt && now < EndsAt;
}
