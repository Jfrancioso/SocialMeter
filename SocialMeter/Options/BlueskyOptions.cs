namespace SocialMeter.Options;

/// <summary>
/// Bluesky via the AT Protocol. Free to read, but search needs a signed-in account: create an app password in Bluesky's
/// settings and store the handle and app password (never the account password). The source is skipped without them.
/// </summary>
public sealed class BlueskyOptions
{
    public const string SectionName = "Bluesky";

    /// <summary>The account's server; it signs in and forwards reads to Bluesky's index.</summary>
    public string ServiceUrl { get; set; } = "https://bsky.social";

    /// <summary>The account handle, e.g. "chatterbub.bsky.social".</summary>
    public string Identifier { get; set; } = "";

    /// <summary>An app password from Bluesky's settings. Revocable without touching the account's own password.</summary>
    public string AppPassword { get; set; } = "";

    /// <summary>
    /// Posts kept per pull, the most-liked first. Every new post is read by Claude, so this is the cost dial:
    /// at roughly a third of a cent per post on Opus, 40 is about 14 cents per pull at most.
    /// </summary>
    public int MaxResults { get; set; } = 40;

    /// <summary>Posts scanned per pull (100 per page) before filtering down to MaxResults. Searching is free.</summary>
    public int ScanLimit { get; set; } = 300;

    /// <summary>How far back a pull looks.</summary>
    public int SearchDays { get; set; } = 7;

    /// <summary>Promo and listing phrases, the same ones X filters out; matching posts are dropped before Claude sees them.</summary>
    public List<string> ExcludeTerms { get; set; } = new()
    {
        "box office", "tickets", "presale", "showtimes", "now streaming", "now available", "premiere", "poster",
        "interview", "release date", "official", "giveaway"
    };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Identifier) && !string.IsNullOrWhiteSpace(AppPassword);
}
