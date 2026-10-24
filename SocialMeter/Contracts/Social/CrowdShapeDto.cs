namespace SocialMeter.Contracts.Social;

/// <summary>
/// "The crowd" for a title page: every counted post as a dot (where it sits and how much it weighs, no text) and the
/// shape of the whole. <see cref="Enabled"/> is false, with nothing else filled in, for titles outside the pilot.
/// </summary>
public sealed class CrowdShapeDto
{
    public bool Enabled { get; init; }

    /// <summary>"split", "loved", "panned", "mixed", "leaning-positive", "leaning-negative" or "early" (see CrowdShapeRules).</summary>
    public string Shape { get; init; } = "";

    /// <summary>Counted posts behind the shape (every one, even when fewer are drawn).</summary>
    public int Posts { get; init; }

    /// <summary>Shares of the counted weight, 0-100: loved it, hated it, in between.</summary>
    public double Positive { get; init; }
    public double Negative { get; init; }
    public double Neutral { get; init; }

    public IReadOnlyList<CrowdDotDto> Dots { get; init; } = Array.Empty<CrowdDotDto>();
}

public sealed class CrowdDotDto
{
    /// <summary>The post's id, for GET /api/socialmeter/{type}/{id}/posts/{postId} when the dot is clicked.</summary>
    public int Id { get; init; }

    public string Platform { get; init; } = "";
    public string Sentiment { get; init; } = "";

    /// <summary>What the post says, -1 (hated it) to +1 (loved it): the score's own per-post value.</summary>
    public double Value { get; init; }

    /// <summary>The post's share of the title's counted weight, 0-100.</summary>
    public double Share { get; init; }
}
