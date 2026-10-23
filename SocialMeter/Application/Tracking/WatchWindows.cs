using Microsoft.Extensions.Options;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Application.Tracking;

/// <summary>
/// The watch windows from configuration (Watch:Windows): a title, a time span in UTC, and how often to read it. While a
/// window is open its title counts as tracked (TitleTracker asks here first) and WatchWindowWorker reads it on schedule.
/// Reads the options live, so a changed App Service setting applies after the restart it causes, with no deploy.
/// </summary>
public sealed class WatchWindows
{
    /// <summary>A window never reads more often than this: the forced-refresh cool-down would turn faster reads away.</summary>
    public const int MinEveryMinutes = 15;

    private readonly IOptionsMonitor<WatchOptions> _options;
    private readonly TimeProvider _clock;

    public WatchWindows(IOptionsMonitor<WatchOptions> options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public IReadOnlyList<WatchWindow> All => _options.CurrentValue.Windows;

    public IReadOnlyList<WatchWindow> Open()
    {
        var now = _clock.GetUtcNow();
        return All.Where(w => w.TmdbId > 0 && w.IsOpen(now)).ToList();
    }

    /// <summary>The open window for this title, if there is one.</summary>
    public WatchWindow? OpenFor(MediaType mediaType, int tmdbId) =>
        Open().FirstOrDefault(w => w.MediaType == mediaType && w.TmdbId == tmdbId);

    /// <summary>The tracked reason shown on the title page while the window is open.</summary>
    public static string ReasonFor(WatchWindow w) => string.IsNullOrWhiteSpace(w.Label) ? "Live event" : w.Label.Trim();
}
