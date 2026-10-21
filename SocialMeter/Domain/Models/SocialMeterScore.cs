namespace SocialMeter.Domain.Models;

/// <summary>The computed SocialMeter reading for a title.</summary>
public sealed class SocialMeterScore
{
    /// <summary>0–100. 50 is perfectly mixed.</summary>
    public int Score { get; init; }

    /// <summary>Human label for the score band: Electric, Hot, Warm, Lukewarm, Cold.</summary>
    public string Label { get; init; } = "";

    /// <summary>0–1, grows with the number of posts behind the score.</summary>
    public double Confidence { get; init; }

    /// <summary>"low" / "medium" / "high" from <see cref="Confidence"/>.</summary>
    public string ConfidenceLabel { get; init; } = "";

    /// <summary>Posts that count toward the score.</summary>
    public int TotalPosts { get; init; }

    /// <summary>Posts the trust policy set aside (too-new accounts, duplicates, pile-ons). Shown so the filtering is visible, not how.</summary>
    public int FilteredPosts { get; init; }

    public SentimentBreakdown Breakdown { get; init; } = new();

    /// <summary>How the last two weeks compare to the overall score; null when there is not enough recent data.</summary>
    public int? Momentum { get; init; }

    public IReadOnlyList<PlatformSummary> Platforms { get; init; } = Array.Empty<PlatformSummary>();

    public static string LabelFor(int score) => score switch
    {
        >= 80 => "Electric",
        >= 65 => "Hot",
        >= 50 => "Warm",
        >= 35 => "Lukewarm",
        _ => "Cold"
    };

    public static string ConfidenceLabelFor(double confidence) => confidence switch
    {
        >= 0.66 => "high",
        >= 0.33 => "medium",
        _ => "low"
    };
}

public sealed class SentimentBreakdown
{
    public int Positive { get; init; }
    public int Neutral { get; init; }
    public int Negative { get; init; }
}

public sealed class PlatformSummary
{
    public string Platform { get; init; } = "";
    public int Posts { get; init; }
    public int Score { get; init; }
}
