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

    /// <summary>Explicit recommend / don't-recommend from the platform (Steam). Null for free-text posts.</summary>
    public bool? VerdictUp { get; set; }

    /// <summary>Author's playtime at the time of the post (Steam). Null when unknown.</summary>
    public int? EngagementMinutes { get; set; }

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

    /// <summary>0–1: how much actual experience the post contains. A detailed take outweighs "10/10".</summary>
    public double Substance { get; set; } = 0.5;

    /// <summary>Excluded posts are kept for auditing but never count toward the score or show in the feed.</summary>
    public bool Excluded { get; set; }

    /// <summary>The filters' reason for excluding the post; null when they let it count. A hand removal does not touch it.</summary>
    public string? ExclusionReason { get; set; }

    /// <summary>
    /// Set when an operator hid the post by hand (see PostModeration). A hidden post stays excluded whatever the filters
    /// later decide, until it is restored. Every writer of <see cref="Excluded"/> keeps that rule.
    /// </summary>
    public DateTime? HiddenAt { get; set; }

    /// <summary>The reason picked when hiding: sensitive, spam, off-topic, wrong-title or other.</summary>
    public string? HiddenReason { get; set; }

    /// <summary>Hidden by an operator. Not stored; query <see cref="HiddenAt"/> in LINQ.</summary>
    public bool Hidden => HiddenAt is not null;

    /// <summary>
    /// Set when an operator corrected the post's label by hand (see PostModeration.Relabel). While set, re-reads by the
    /// assessor keep <see cref="Sentiment"/> and <see cref="SentimentScore"/> as the operator left them. The assessor's
    /// own label, and the operator's reason, are in the moderation log.
    /// </summary>
    public DateTime? LabelFixedAt { get; set; }

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
