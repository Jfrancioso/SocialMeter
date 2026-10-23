using SocialMeter.Application.Social;

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

    /// <summary>Process-wide ceiling on live pulls per hour. Beyond it, readers get the cached reading and a note saying why.</summary>
    public int MaxLivePullsPerHour { get; set; } = 60;

    /// <summary>Forced refreshes (?refresh=true) one caller may trigger per hour, by client address.</summary>
    public int MaxForcedRefreshesPerHourPerCaller { get; set; } = 6;

    /// <summary>A tracked-set rebuild requested within this many minutes of the last one is answered from the last result.</summary>
    public int MinTrackedRefreshMinutes { get; set; } = 10;

    // ---- Aggregation. Published on the "how it works" page; kept here so they can be tuned against real titles. ----

    /// <summary>
    /// Strength of the pull toward neutral, in effective posts (the k in the Bayesian average). With this many effective
    /// posts the data and the prior count equally; a title needs several times this to reach Electric or Cold.
    /// </summary>
    public double PriorStrength { get; set; } = 6;

    /// <summary>Floor on the per-post spread used for the margin of error, so unanimous posts never claim zero uncertainty. Sentiment units (−1..1).</summary>
    public double MinSigma { get; set; } = 0.2;

    /// <summary>Days of daily readings the trend test looks at.</summary>
    public int TrendWindowDays { get; set; } = 30;

    /// <summary>Significance level for calling a trend "rising" or "falling" (Mann-Kendall p-value).</summary>
    public double TrendAlpha { get; set; } = 0.05;

    /// <summary>Daily readings needed before the trend test is run at all.</summary>
    public int MinTrendPoints { get; set; } = 5;

    /// <summary>When a title has no history yet, this many past days are reconstructed from stored post dates.</summary>
    public int HistoryBackfillDays { get; set; } = 60;

    public ScoringParameters Scoring => new(PriorStrength, MinSigma);

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

    // ---- Playtime trust (Steam reviews). When playtime is known it replaces the account-age signal. ----

    /// <summary>Reviews written with less playtime than this are excluded — refund-window rants and drive-bys.</summary>
    public int MinPlaytimeMinutes { get; set; } = 30;

    /// <summary>Playtime at which a review counts at full weight; it ramps up from the minimum.</summary>
    public int FullPlaytimeMinutes { get; set; } = 180;

    /// <summary>Steam reviews can't be re-checked individually, so they are kept for a shorter window than posts.</summary>
    public int SteamReviewRetentionDays { get; set; } = 120;

    /// <summary>Refresh-log rows (title, time, outcome; nothing about the visitor) are deleted after this many days.</summary>
    public int RefreshLogRetentionDays { get; set; } = 90;

    /// <summary>The home page leads with the tracked title with the most counted posts over this many days...</summary>
    public int FeaturedWindowDays { get; set; } = 3;

    /// <summary>...as long as it has at least this many counted posts in the window...</summary>
    public int FeaturedMinPosts { get; set; } = 10;

    /// <summary>...and its reading's confidence (0-1, how much of the score is posts rather than the pull toward 50) is at least this.</summary>
    public double FeaturedMinConfidence { get; set; } = 0.6;
}
