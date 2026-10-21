namespace SocialMeter.Domain.Models;

/// <summary>One post pulled from a social platform about a title, with its analyzed sentiment and trust. Persisted.</summary>
public sealed class SocialPost
{
    public int Id { get; set; }

    public int TmdbId { get; set; }
    public MediaType MediaType { get; set; }

    /// <summary>"reddit" or "x".</summary>
    public string Platform { get; set; } = "";

    /// <summary>The platform's own id for the post; unique per platform.</summary>
    public string ExternalId { get; set; } = "";

    public string Author { get; set; } = "";
    public string? AuthorId { get; set; }
    public DateTime? AuthorCreatedAt { get; set; }
    public int? AuthorReputation { get; set; }

    public string Content { get; set; } = "";

    /// <summary>Hash of the normalized content, used to catch copy-paste campaigns.</summary>
    public string ContentHash { get; set; } = "";

    public Sentiment Sentiment { get; set; }
    public double SentimentScore { get; set; }

    /// <summary>0–1 multiplier from the trust policy (account age, reputation, content quality). Never exposed raw.</summary>
    public double TrustWeight { get; set; } = 1;

    /// <summary>Excluded posts are kept for auditing but never count toward the score or show in the feed.</summary>
    public bool Excluded { get; set; }
    public string? ExclusionReason { get; set; }

    public int Upvotes { get; set; }
    public int Comments { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime FetchedAt { get; set; }

    public string? SourceUrl { get; set; }

    /// <summary>Subreddit for Reddit posts; null for other platforms.</summary>
    public string? Community { get; set; }
}
