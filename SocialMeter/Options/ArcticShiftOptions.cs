namespace SocialMeter.Options;

/// <summary>
/// Arctic Shift (arctic-shift.photon-reddit.com): a volunteer-run, keyless archive of Reddit, used ONLY for local testing
/// and evaluation while the official Reddit API application is pending. It is never registered outside Development:
/// the archive scrapes Reddit against its terms, and running the product on it would put the official approval at risk.
/// </summary>
public sealed class ArcticShiftOptions
{
    public const string SectionName = "ArcticShift";

    /// <summary>Off unless set in appsettings.Development.json or user secrets. Ignored outside Development.</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://arctic-shift.photon-reddit.com/";

    public string UserAgent { get; set; } = "SocialMeter local evaluation (by /u/socialmeter)";

    /// <summary>
    /// How far back the comment search looks. The archive's full-text search over a big subreddit times out when the
    /// window is wide, so the source halves this until the archive answers (never below a week). It lags live Reddit by a day or two.
    /// </summary>
    public int Days { get; set; } = 90;

    /// <summary>Comments per search call (the archive caps at 100; smaller pages answer faster).</summary>
    public int Limit { get; set; } = 50;

    /// <summary>Discussion threads to expand per title: comments in an "official discussion" thread rarely repeat the title's name.</summary>
    public int MaxThreads { get; set; } = 2;

    /// <summary>
    /// Subreddits searched per media type. The big general ones hold the official discussion threads and answer reliably;
    /// genre subs (horror, TrueFilm) timed out on every attempt in testing, so add them only when the archive is quiet.
    /// </summary>
    public List<string> MovieSubreddits { get; set; } = new() { "movies" };
    public List<string> TvSubreddits { get; set; } = new() { "television" };
    public List<string> GameSubreddits { get; set; } = new() { "Games" };

    /// <summary>Pause between archive calls. Its limits are dynamic and it answers 429 quickly when crowded.</summary>
    public int PauseMilliseconds { get; set; } = 2000;
}
