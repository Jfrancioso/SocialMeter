namespace SocialMeter.Domain.Models;

/// <summary>The computed SocialMeter reading for a title.</summary>
public sealed class SocialMeterScore
{
    /// <summary>0–100 after the Bayesian pull toward neutral. 50 is perfectly mixed.</summary>
    public int Score { get; init; }

    /// <summary>Human label for the score band: Electric, Hot, Warm, Lukewarm, Cold.</summary>
    public string Label { get; init; } = "";

    /// <summary>0–1: the share of the score that comes from the posts rather than the neutral prior (n / (n + k)).</summary>
    public double Confidence { get; init; }

    /// <summary>"low" / "medium" / "high" from <see cref="Confidence"/>.</summary>
    public string ConfidenceLabel { get; init; } = "";

    /// <summary>Posts that count toward the score.</summary>
    public int TotalPosts { get; init; }

    /// <summary>Posts the trust policy set aside (too-new accounts, duplicates, pile-ons). Shown so the filtering is visible, not how.</summary>
    public int FilteredPosts { get; init; }

    /// <summary>Counted posts with real substance (specific scenes, mechanics, an argument) rather than a bare reaction.</summary>
    public int DetailedPosts { get; init; }

    public SentimentBreakdown Breakdown { get; init; } = new();

    /// <summary>The numbers behind the score, so it can be checked rather than believed.</summary>
    public ScoreStats Stats { get; init; } = new();

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
        >= 0.8 => "high",
        >= 0.5 => "medium",
        _ => "low"
    };
}

public sealed class ScoreStats
{
    /// <summary>The plain weighted mean on the 0–100 scale, before the pull toward neutral.</summary>
    public int RawScore { get; init; } = 50;

    /// <summary>
    /// How many full-weight posts the counted set is worth: Kish's effective sample size of the weights, scaled by the
    /// average trust-and-substance credit. Forty bot-ish posts are worth a few; one viral post among many is worth one.
    /// </summary>
    public double EffectivePosts { get; init; }

    /// <summary>Half-width of the 95 % interval around <see cref="SocialMeterScore.Score"/>, in points. 50 means "could be anywhere".</summary>
    public int Margin { get; init; } = 50;

    public int Lower { get; init; } = 0;
    public int Upper { get; init; } = 100;

    /// <summary>Points the Bayesian prior moved the raw score toward 50 (negative when it pulled down).</summary>
    public int PriorPull { get; init; }
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

    /// <summary>The platform's own reading, same maths as the headline score.</summary>
    public int Score { get; init; }
}
