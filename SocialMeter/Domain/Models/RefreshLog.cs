namespace SocialMeter.Domain.Models;

/// <summary>
/// One live-pull decision: a reader pressed Refresh, or a page view found a tracked title stale and pulled. Records the
/// title, the time and what happened, and deliberately nothing about who asked (no IP address, no device). Persisted;
/// pruned after SocialMeter:RefreshLogRetentionDays.
/// </summary>
public sealed class RefreshLog
{
    public long Id { get; set; }

    /// <summary>When the request was handled, UTC.</summary>
    public DateTime At { get; set; }

    public MediaType MediaType { get; set; }
    public int TmdbId { get; set; }

    /// <summary>The title's name at the time, so a query reads without a TMDB lookup.</summary>
    public string TitleName { get; set; } = "";

    /// <summary>"button" when a reader pressed Refresh; "page" when a page view found the title stale.</summary>
    public string Trigger { get; set; } = "";

    /// <summary>Whether the sources were actually contacted (this is what spends X credits).</summary>
    public bool Pulled { get; set; }

    /// <summary>pulled, cooldown, not-tracked or budget-reached.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>Posts the sources returned on this pull; null when nothing was pulled.</summary>
    public int? PostsFetched { get; set; }
}
