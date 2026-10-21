using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Social;

/// <summary>Everything behind one reading, in enough detail to recompute it. Thresholds and raw weights stay out.</summary>
public sealed class ScoreExplanationDto
{
    public int Score { get; init; }
    public string Label { get; init; } = "";
    public ScoreStats Stats { get; init; } = new();
    public double Confidence { get; init; }
    public string ConfidenceLabel { get; init; } = "";
    public int TotalPosts { get; init; }
    public int FilteredPosts { get; init; }

    /// <summary>Share of the total weight (0–100) carried by posts read as positive / neutral / negative.</summary>
    public ShareDto SentimentWeight { get; init; } = new();

    /// <summary>Posts and weight share by how much substance they carry.</summary>
    public IReadOnlyList<MixDto> SubstanceMix { get; init; } = Array.Empty<MixDto>();

    public IReadOnlyList<PlatformShareDto> Platforms { get; init; } = Array.Empty<PlatformShareDto>();

    /// <summary>The posts that pull hardest, with their share of the weight.</summary>
    public IReadOnlyList<DriverDto> Drivers { get; init; } = Array.Empty<DriverDto>();

    /// <summary>Why set-aside posts were set aside, by count.</summary>
    public IReadOnlyList<ReasonCountDto> Filtered { get; init; } = Array.Empty<ReasonCountDto>();

    /// <summary>Plain-English observations derived from the numbers above.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed class ShareDto
{
    public double Positive { get; init; }
    public double Neutral { get; init; }
    public double Negative { get; init; }
}

public sealed class MixDto
{
    /// <summary>"bare" / "some" / "detailed".</summary>
    public string Level { get; init; } = "";
    public int Posts { get; init; }
    public double WeightShare { get; init; }
}

public sealed class PlatformShareDto
{
    public string Platform { get; init; } = "";
    public int Posts { get; init; }
    public double WeightShare { get; init; }
    public int Score { get; init; }
}

public sealed class DriverDto
{
    public int Id { get; init; }
    public string Platform { get; init; } = "";
    public string Author { get; init; } = "";
    public string Excerpt { get; init; } = "";
    public Sentiment Sentiment { get; init; }
    public double SentimentScore { get; init; }
    public string SubstanceLevel { get; init; } = "";
    public string TrustLevel { get; init; } = "";
    public double WeightShare { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? SourceUrl { get; init; }
}

public sealed class ReasonCountDto
{
    public string Reason { get; init; } = "";
    public int Count { get; init; }
}
