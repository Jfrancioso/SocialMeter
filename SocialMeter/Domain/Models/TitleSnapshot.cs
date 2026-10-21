namespace SocialMeter.Domain.Models;

/// <summary>Cached SocialMeter result for a title, refreshed when it goes stale. Persisted.</summary>
public sealed class TitleSnapshot
{
    public int Id { get; set; }

    public int TmdbId { get; set; }
    public MediaType MediaType { get; set; }

    public int Score { get; set; }
    public string Label { get; set; } = "";
    public double Confidence { get; set; }

    public int TotalPosts { get; set; }

    /// <summary>Posts the trust policy excluded (new accounts, duplicates, pile-ons). Not part of TotalPosts.</summary>
    public int FilteredPosts { get; set; }

    public int Positive { get; set; }
    public int Neutral { get; set; }
    public int Negative { get; set; }

    /// <summary>Score change between recent posts and the overall score; null when there is not enough recent data.</summary>
    public int? Momentum { get; set; }

    public DateTime LastRefreshedAt { get; set; }
}
