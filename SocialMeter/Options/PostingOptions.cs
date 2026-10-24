namespace SocialMeter.Options;

/// <summary>
/// The brand account's posts (the Outbox): the site drafts, an admin approves each one. Nothing is ever posted without
/// an admin's tap. Turn it all off (no drafts, Post refused) with Posting__Enabled=false.
/// </summary>
public sealed class PostingOptions
{
    public const string SectionName = "Posting";

    public bool Enabled { get; set; } = true;

    /// <summary>Posts sent per UTC day, all kinds together. A thread (the post and its two replies) counts once.</summary>
    public int MaxPerDay { get; set; } = 2;

    /// <summary>The daily draft is made at or after this hour, UTC (14 = 10 am Eastern).</summary>
    public int DraftHourUtc { get; set; } = 14;

    /// <summary>A title isn't drafted again (except for a live event) within this many days.</summary>
    public int RepeatAfterDays { get; set; } = 7;

    /// <summary>Drafts nobody acted on expire after this many hours; their moment has passed.</summary>
    public int ExpireAfterHours { get; set; } = 36;

    /// <summary>Where the posts link to.</summary>
    public string SiteUrl { get; set; } = "https://chatterbub.com";

    /// <summary>The brand's Bluesky account. Kept in App Service settings, never appsettings (the repo is public).</summary>
    public BlueskyPostingAccount Bluesky { get; set; } = new();
}

public sealed class BlueskyPostingAccount
{
    public string ServiceUrl { get; set; } = "https://bsky.social";

    /// <summary>The brand's handle, e.g. "chatterbub.bsky.social".</summary>
    public string Identifier { get; set; } = "";

    /// <summary>An app password made for the site in Bluesky's settings; revocable without touching the account password.</summary>
    public string AppPassword { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Identifier) && !string.IsNullOrWhiteSpace(AppPassword);
}
