namespace SocialMeter.Options;

public sealed class SocialMeterOptions
{
    public const string SectionName = "SocialMeter";

    /// <summary>A title's score is recomputed from fresh social posts once it is older than this.</summary>
    public int RefreshAfterHours { get; set; } = 6;

    /// <summary>
    /// A forced refresh (?refresh=true) within this many minutes of the last pull serves the cache instead.
    /// Each live pull costs X API credits, so this stops a double-tap or a reload loop from spending them.
    /// </summary>
    public int MinForcedRefreshMinutes { get; set; } = 15;

    /// <summary>How many posts the score endpoint returns inline.</summary>
    public int TopPosts { get; set; } = 12;

    /// <summary>Posts newer than this count toward momentum.</summary>
    public int MomentumWindowDays { get; set; } = 14;

    // ---- Tracked set: only these titles get social pulls. ----

    /// <summary>How often the tracked set is rebuilt from TMDB's lists.</summary>
    public int TrackedRefreshHours { get; set; } = 6;

    /// <summary>A title stays tracked this long after it last appeared on a qualifying list, so it doesn't flicker.</summary>
    public int TrackedGraceHours { get; set; } = 48;

    /// <summary>Pages of each TMDB list to include (20 titles per page).</summary>
    public int TrackedListPages { get; set; } = 2;

    /// <summary>A title with at least this reading, on at least ElectricMinPosts posts, stays tracked on merit.</summary>
    public int ElectricMinScore { get; set; } = 80;
    public int ElectricMinPosts { get; set; } = 5;

    // ---- Deletion sync: content removed at the source must not linger here. ----

    /// <summary>How often the background job re-checks stored posts against their platform.</summary>
    public int DeletionSyncHours { get; set; } = 12;

    /// <summary>A post is re-checked once it has gone this long without confirmation that it still exists.</summary>
    public int RecheckAfterHours { get; set; } = 24;

    /// <summary>Posts older than this are dropped outright; the score only looks back about a year anyway.</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>Upper bound on posts checked per run, to stay well inside platform rate limits.</summary>
    public int DeletionSyncBatch { get; set; } = 2000;

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
