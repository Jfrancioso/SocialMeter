namespace SocialMeter.Domain.Models;

/// <summary>
/// One hand moderation step: a post hidden, restored or relabelled by an operator. Kept as an audit trail, for undo, and
/// as the record of the assessor's mistakes (a relabel keeps what it said, what the operator changed it to and why).
/// Holds no copy of the post's text, so a post later deleted at its source is gone from here too (only its ids remain).
/// Persisted.
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

    /// <summary>
    /// "hide", "restore", "relabel" or "undo-relabel" (the constants below). The admin page reads these words too.
    /// </summary>
    public string Action { get; set; } = "";

    public const string Hide = "hide";
    public const string Restore = "restore";
    public const string Relabel = "relabel";
    public const string UndoRelabel = "undo-relabel";

    /// <summary>The reason given when hiding (see PostModeration.Reasons); null for a restore.</summary>
    public string? Reason { get; set; }

    /// <summary>Who did it: the signed-in operator's email, or "admin key" for a script.</summary>
    public string Actor { get; set; } = "";

    /// <summary>A relabel: the label and tone before ("negative", -0.6 as the assessor read it) and the label after.</summary>
    public string? FromLabel { get; set; }
    public double? FromScore { get; set; }
    public string? ToLabel { get; set; }

    /// <summary>A relabel: the operator's own words on why ("quotes a clip about another movie"). Shown only to operators.</summary>
    public string? Note { get; set; }
}
