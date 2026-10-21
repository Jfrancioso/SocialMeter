namespace SocialMeter.Domain.Models;

public enum Sentiment
{
    Negative,
    Neutral,
    Positive
}

/// <summary>Result of analyzing one piece of text. Score runs from -1 (hostile) to +1 (glowing).</summary>
public readonly record struct SentimentResult(Sentiment Label, double Score)
{
    public static SentimentResult FromScore(double score)
    {
        var clamped = Math.Clamp(score, -1, 1);
        var label = clamped switch
        {
            >= 0.15 => Sentiment.Positive,
            <= -0.15 => Sentiment.Negative,
            _ => Sentiment.Neutral
        };
        return new SentimentResult(label, clamped);
    }
}
