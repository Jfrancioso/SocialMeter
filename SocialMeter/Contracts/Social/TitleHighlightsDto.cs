namespace SocialMeter.Contracts.Social;

/// <summary>
/// The title page's "In their words": the featured post on each side. Both sides are null when the title has no
/// counted posts; otherwise each side says how big it is, so the page can explain a side with no featured post.
/// </summary>
public sealed class TitleHighlightsDto
{
    public HighlightDto? Positive { get; init; }
    public HighlightDto? Negative { get; init; }
}

public sealed class HighlightDto
{
    /// <summary>Counted posts on this side.</summary>
    public int Posts { get; init; }

    /// <summary>This side's share of the title's counted weight, 0–100.</summary>
    public double WeightShare { get; init; }

    /// <summary>False when the side is too small a part of the reading to feature (see HighlightRules.Featured).</summary>
    public bool Featured { get; init; }

    /// <summary>The featured post, exactly as posted. Null when the side isn't featured or none of its posts reads well on its own.</summary>
    public SocialPostDto? Post { get; init; }
}
