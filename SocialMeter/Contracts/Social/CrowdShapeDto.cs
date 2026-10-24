namespace SocialMeter.Contracts.Social;

/// <summary>
/// "The crowd" for a title page: every counted post as a dot (where it sits and how much it weighs, no text), the shape
/// of the whole, and the crowd over time. <see cref="Enabled"/> is false, with nothing else filled in, for titles that
/// don't get one (see CrowdShapeService).
/// </summary>
public sealed record CrowdShapeDto
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

    /// <summary>
    /// The crowd over time: one moment per post of the last <see cref="TimelineHours"/> hours, in the order they were
    /// posted (see CrowdShapeRules.Timeline). Empty when there are too few recent posts to draw a line.
    /// </summary>
    public IReadOnlyList<CrowdMomentDto> Timeline { get; init; } = Array.Empty<CrowdMomentDto>();
    public int TimelineHours { get; init; }

    /// <summary>A watch window is open for this title right now (a live event): the page checks back for new posts.</summary>
    public bool Live { get; init; }

    /// <summary>The live event's label ("Watch party tonight"); null when not live.</summary>
    public string? LiveLabel { get; init; }
}

public sealed record CrowdDotDto
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

/// <summary>One post on the crowd's timeline: when it was posted, and the mood of the latest posts up to it (0-100).</summary>
public sealed record CrowdMomentDto
{
    public DateTime At { get; init; }
    public int Mood { get; init; }
    public string Sentiment { get; init; } = "";
    public string Platform { get; init; } = "";
}
