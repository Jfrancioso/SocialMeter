namespace SocialMeter.Options;

public sealed class SocialMeterOptions
{
    public const string SectionName = "SocialMeter";

    /// <summary>A title's score is recomputed from fresh social posts once it is older than this.</summary>
    public int RefreshAfterHours { get; set; } = 6;

    /// <summary>How many posts the score endpoint returns inline.</summary>
    public int TopPosts { get; set; } = 12;

    /// <summary>Posts newer than this count toward momentum.</summary>
    public int MomentumWindowDays { get; set; } = 14;

    // ---- Trust policy. Exact values stay server-side on purpose; the public "how it works" page describes them only in broad strokes. ----

    /// <summary>Accounts younger than this are excluded outright — the cheapest spam to create is a fresh account.</summary>
    public int MinAccountAgeDays { get; set; } = 30;

    /// <summary>Accounts at least this old count at full weight; between the minimum and this they ramp up.</summary>
    public int EstablishedAccountDays { get; set; } = 365;

    /// <summary>Reddit karma / X followers below this halves a post's weight.</summary>
    public int MinReputation { get; set; } = 10;

    /// <summary>Weight for posts whose account age the source could not report.</summary>
    public double UnknownAccountWeight { get; set; } = 0.6;

    /// <summary>Posts with fewer meaningful characters than this are excluded ("🔥🔥🔥", "lol").</summary>
    public int MinContentLength { get; set; } = 12;

    /// <summary>Posts carrying this many links or more are treated as promotional and halved.</summary>
    public int LinkSpamThreshold { get; set; } = 3;
}
