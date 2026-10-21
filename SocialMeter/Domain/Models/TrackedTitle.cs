namespace SocialMeter.Domain.Models;

/// <summary>
/// A title SocialMeter is currently measuring. Only tracked titles get social pulls, which is what keeps the
/// X bill bounded: the set is a few hundred titles that are current, trending, seasonal, or already electric —
/// never the whole catalogue. Persisted; rebuilt on a schedule with a grace period so titles don't flicker.
/// </summary>
public sealed class TrackedTitle
{
    public int Id { get; set; }
    public int TmdbId { get; set; }
    public MediaType MediaType { get; set; }

    /// <summary>Why it is tracked: "In theaters", "On the air", "Trending", "Halloween picks", "Electric", …</summary>
    public string Reason { get; set; } = "";

    public DateTime Since { get; set; }

    /// <summary>Last refresh that found it on a qualifying list. It drops out once this is older than the grace period.</summary>
    public DateTime LastSeenAt { get; set; }
}
