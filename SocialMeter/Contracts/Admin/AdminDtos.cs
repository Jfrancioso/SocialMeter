using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Admin;

/// <summary>Who the admin endpoints think is calling.</summary>
public sealed class AdminMeDto
{
    /// <summary>The signed-in operator's email, or "admin key".</summary>
    public string Actor { get; init; } = "";
}

public sealed class HidePostRequest
{
    /// <summary>One of PostModeration.Reasons: sensitive, spam, off-topic, wrong-title, other.</summary>
    public string Reason { get; init; } = "";

    /// <summary>The title's name, for the log. Optional.</summary>
    public string? TitleName { get; init; }
}

public sealed class RelabelPostRequest
{
    /// <summary>"positive", "neutral" or "negative".</summary>
    public string Sentiment { get; init; } = "";

    /// <summary>Why, in the operator's own words (3-300 characters). Kept for the assessor's accuracy test.</summary>
    public string Note { get; init; } = "";

    /// <summary>The title's name, for the log. Optional.</summary>
    public string? TitleName { get; init; }
}

/// <summary>The result of a moderation step: the post's state and the title's score afterwards.</summary>
public sealed class ModerationResultDto
{
    public int PostId { get; init; }
    public bool Hidden { get; init; }

    /// <summary>The post's label now ("positive", "neutral", "negative"), and whether an operator fixed it.</summary>
    public string Sentiment { get; init; } = "";
    public bool LabelFixed { get; init; }

    /// <summary>False when the post was already in the requested state.</summary>
    public bool Changed { get; init; }

    /// <summary>The recomputed score; null when the title has never been scored.</summary>
    public int? Score { get; init; }
    public string? Label { get; init; }

    /// <summary>The whole recomputed reading (same shape as the title page's), when this call changed it.</summary>
    public SocialMeterScore? Meter { get; init; }
}

/// <summary>One line of the moderation log.</summary>
public sealed class ModerationEntryDto
{
    public long Id { get; init; }
    public DateTime At { get; init; }
    public string Action { get; init; } = "";
    public string? Reason { get; init; }
    public string Actor { get; init; } = "";

    public int PostId { get; init; }
    public MediaType MediaType { get; init; }
    public int TmdbId { get; init; }
    public string TitleName { get; init; } = "";

    /// <summary>The post as it is now; null fields when it has since been deleted at its source.</summary>
    public string? Platform { get; init; }
    public string? Author { get; init; }
    public string? Excerpt { get; init; }
    public string? SourceUrl { get; init; }

    /// <summary>Whether the post is hidden right now (so the log can offer Restore only where it applies).</summary>
    public bool CurrentlyHidden { get; init; }

    /// <summary>Whether the post's label is fixed by hand right now (so the log can offer Undo on a relabel).</summary>
    public bool CurrentlyRelabeled { get; init; }

    /// <summary>A relabel or its undo: the label before and after, and the operator's reason.</summary>
    public string? FromLabel { get; init; }
    public string? ToLabel { get; init; }
    public string? Note { get; init; }

    /// <summary>The post no longer exists here (deleted at its source and purged).</summary>
    public bool PostGone { get; init; }
}
