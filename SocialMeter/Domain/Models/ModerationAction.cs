namespace SocialMeter.Domain.Models;

/// <summary>
/// One hand moderation step: a post hidden or restored by an operator. Kept as an audit trail and for undo. Holds no
/// copy of the post's text, so a post later deleted at its source is gone from here too (only its ids remain). Persisted.
/// </summary>
public sealed class ModerationAction
{
    public long Id { get; set; }

    /// <summary>When it happened, UTC.</summary>
    public DateTime At { get; set; }

    /// <summary>The post's id. Not a foreign key: the deletion sync may purge the post and the record must survive it.</summary>
    public int PostId { get; set; }

    public MediaType MediaType { get; set; }
    public int TmdbId { get; set; }

    /// <summary>The title's name at the time, so the log reads without a lookup. Empty when the caller did not send it.</summary>
    public string TitleName { get; set; } = "";

    /// <summary>"hide" or "restore" (<see cref="Hide"/>, <see cref="Restore"/>). The admin page reads these words too.</summary>
    public string Action { get; set; } = "";

    public const string Hide = "hide";
    public const string Restore = "restore";

    /// <summary>The reason given when hiding (see PostModeration.Reasons); null for a restore.</summary>
    public string? Reason { get; set; }

    /// <summary>Who did it: the signed-in operator's email, or "admin key" for a script.</summary>
    public string Actor { get; set; } = "";
}
