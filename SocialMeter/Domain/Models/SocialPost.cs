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

    /// <summary>Attached media as a JSON array of <see cref="PostMedia"/>; null when the post has none.</summary>
    public string? MediaJson { get; set; }

    /// <summary>The platform flagged the post as possibly sensitive; the UI hides its media behind a tap.</summary>
    public bool Sensitive { get; set; }

    /// <summary>Hash of the normalized content, used to catch copy-paste campaigns.</summary>
    public string ContentHash { get; set; } = "";

    public Sentiment Sentiment { get; set; }
    public double SentimentScore { get; set; }

    /// <summary>Which assessor produced the sentiment ("lexicon", "claude"); lets a better assessor find posts it hasn't read.</summary>
    public string Analyzer { get; set; } = "lexicon";

    /// <summary>The assessor's short reason, for auditing. Never shown to readers.</summary>
    public string? AnalyzerNote { get; set; }

    /// <summary>0–1 multiplier from the trust policy (account age, reputation, content quality). Never exposed raw.</summary>
    public double TrustWeight { get; set; } = 1;

    /// <summary>Excluded posts are kept for auditing but never count toward the score or show in the feed.</summary>
    public bool Excluded { get; set; }
    public string? ExclusionReason { get; set; }

    public int Upvotes { get; set; }
    public int Comments { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime FetchedAt { get; set; }

    /// <summary>When the deletion sync last confirmed this post still exists at the source. Null until first checked.</summary>
    public DateTime? LastCheckedAt { get; set; }

    public string? SourceUrl { get; set; }

    /// <summary>Subreddit for Reddit posts; null for other platforms.</summary>
    public string? Community { get; set; }
}
