namespace SocialMeter.Domain.Models;

/// <summary>A post as returned by a social source, before trust evaluation, sentiment analysis and persistence.</summary>
public sealed record SocialPostCandidate(
    string Platform,
    string ExternalId,
    string Author,
    string Content,
    int Upvotes,
    int Comments,
    DateTime CreatedAt,
    string? SourceUrl,
    string? Community,
    /// <summary>The platform's stable id for the author (Reddit t2_ fullname, X user id, Steam id). Null when unavailable.</summary>
    string? AuthorId = null,
    /// <summary>When the author's account was created. Null when the source could not tell us.</summary>
    DateTime? AuthorCreatedAt = null,
    /// <summary>Reddit karma, X follower count or Steam games owned — a rough "has this account participated" signal.</summary>
    int? AuthorReputation = null,
    /// <summary>Attached images (or video preview frames). Often where the actual opinion lives.</summary>
    IReadOnlyList<PostMedia>? Media = null,
    /// <summary>The platform flagged the post as possibly sensitive; its media is hidden by default.</summary>
    bool Sensitive = false,
    /// <summary>An explicit thumbs up/down from the platform (Steam reviews). When present, no model guesses the sentiment.</summary>
    bool? VerdictUp = null,
    /// <summary>How long the author has used the title (Steam playtime). The strongest trust signal there is for games.</summary>
    int? EngagementMinutes = null,
    /// <summary>The platform confirms the author bought the title. Null when the platform doesn't say.</summary>
    bool? VerifiedOwner = null,
    /// <summary>The author got the title for free (review key, giveaway) — discounted, not excluded.</summary>
    bool ReceivedForFree = false
);
