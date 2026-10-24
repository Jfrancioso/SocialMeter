namespace SocialMeter.Domain.Models;

/// <summary>
/// One drafted social post for the brand account: a title's facts, linked to its page, with the strongest Bluesky post on
/// each side quoted in replies. Drafted by the site, posted only when an admin taps Post. The numbers are filled in when
/// the draft is shown or posted, so they're never stale; what was actually posted is kept in <see cref="PostedText"/>.
/// Persisted.
/// </summary>
public sealed class OutboxItem
{
    public int Id { get; set; }

    /// <summary>Why this title: "split", "talked-about" or "event" (the constants below).</summary>
    public string Kind { get; set; } = "";

    public const string Split = "split", TalkedAbout = "talked-about", Event = "event";

    public MediaType MediaType { get; set; }
    public int TmdbId { get; set; }

    /// <summary>The title's name when drafted, so the outbox reads without a lookup.</summary>
    public string TitleName { get; set; } = "";

    /// <summary>The Bluesky posts to quote as the case for and against (SocialPost ids); null when a side had none.</summary>
    public int? ForPostId { get; set; }
    public int? AgainstPostId { get; set; }

    /// <summary>"draft", "posted", "skipped" or "expired" (the constants below).</summary>
    public string Status { get; set; } = Draft;

    public const string Draft = "draft", Posted = "posted", Skipped = "skipped", Expired = "expired";

    public DateTime CreatedAt { get; set; }

    /// <summary>When an admin posted or skipped it, and who.</summary>
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }

    /// <summary>The main post as published, and its Bluesky address (at://...).</summary>
    public string? PostedText { get; set; }
    public string? PostedUri { get; set; }

    /// <summary>Why the last Post attempt failed, in a few words; the draft stays a draft so it can be tried again.</summary>
    public string? Error { get; set; }
}
