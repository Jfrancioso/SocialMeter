using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Outbox;

/// <summary>
/// What the brand account posts, as pure rules: the wording (facts only, then the link), which title the daily draft is
/// about, and when drafts are due. The service (OutboxService) supplies the data and keeps the records.
/// </summary>
public static class OutboxRules
{
    /// <summary>
    /// Bluesky allows 300 characters; X 280 with a link counted as 23. Staying under this fits both, so "Copy for X" never
    /// needs trimming.
    /// </summary>
    public const int MaxTextLength = 240;

    /// <summary>A title's name is cut to this length in posts.</summary>
    public const int MaxNameLength = 70;

    /// <summary>The quoted posts' leads, in reply order.</summary>
    public const string CaseFor = "The case for:", CaseAgainst = "The case against:";

    /// <summary>The facts a post states. Shares are percentages of the counted weight; null when the crowd isn't drawn.</summary>
    public sealed record Facts(string Name, int? Year, int Score, string Label, int Posts, double? Loved, double? Hated, string? LiveLabel);

    /// <summary>
    /// Facts only, then the link (shown without "https://"):
    /// "Buddy (2026)\nChatterbub score 69 · Hot\n74% loved it · 22% hated it · 36 posts\nchatterbub.com/title/movie/1514026".
    /// A live event leads with its label.
    /// </summary>
    public static string Text(Facts f, string link)
    {
        var name = f.Name.Length > MaxNameLength ? f.Name[..(MaxNameLength - 1)].TrimEnd() + "…" : f.Name;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.LiveLabel)) lines.Add($"Live now: {f.LiveLabel.Trim()}");
        lines.Add(f.Year is { } y ? $"{name} ({y})" : name);
        lines.Add($"Chatterbub score {f.Score} · {f.Label}");
        var posts = $"{f.Posts} {(f.Posts == 1 ? "post" : "posts")}";
        lines.Add(f.Loved is { } loved && f.Hated is { } hated
            ? $"{Pct(loved)}% loved it · {Pct(hated)}% hated it · {posts}"
            : posts);
        lines.Add(link);
        return string.Join("\n", lines);
    }

    /// <summary>The link as it reads in a post: "chatterbub.com/title/movie/1514026".</summary>
    public static string Display(string url) => url.Replace("https://", "").Replace("http://", "");

    public static string TitleUrl(string siteUrl, MediaType mediaType, int tmdbId) =>
        $"{siteUrl.TrimEnd('/')}/title/{mediaType.ToString().ToLowerInvariant()}/{tmdbId}";

    /// <summary>
    /// A candidate for the daily draft: a most-talked-about title (best first), its crowd's shape, and how many sides
    /// have a Bluesky post that can be quoted (0-2).
    /// </summary>
    public sealed record Candidate(MediaType MediaType, int TmdbId, string Shape, int QuotableSides = 0);

    /// <summary>
    /// The daily draft's title, never one drafted in the last RepeatAfterDays and never a game (as for the home page's
    /// hero: Steam reviews arrive in bulk and would always win, and Bluesky rarely talks games). Best first: a quotable
    /// Bluesky post on both sides (the case for and against is the point of the post), then a split crowd (the most
    /// interesting thing the site can say), then the most talked about. Null when nothing is left.
    /// </summary>
    public static Candidate? PickDaily(IReadOnlyList<Candidate> ranked, IReadOnlySet<(MediaType, int)> recentlyDrafted) =>
        ranked
            .Where(c => c.MediaType != MediaType.Game && !recentlyDrafted.Contains((c.MediaType, c.TmdbId)))
            .OrderByDescending(c => c.QuotableSides == 2)
            .ThenByDescending(c => c.Shape == Social.CrowdShapeRules.Split)
            .FirstOrDefault();   // OrderBy is stable: ties keep the most-talked-about order

    public static string KindFor(Candidate c) => c.Shape == Social.CrowdShapeRules.Split ? OutboxItem.Split : OutboxItem.TalkedAbout;

    /// <summary>The daily draft is due once a day, at or after the draft hour (UTC).</summary>
    public static bool DailyDue(DateTime nowUtc, DateTime? lastDailyDraftUtc, int draftHourUtc) =>
        nowUtc.Hour >= draftHourUtc && (lastDailyDraftUtc is null || lastDailyDraftUtc.Value.Date < nowUtc.Date);

    private static int Pct(double share) => (int)Math.Round(share);
}
