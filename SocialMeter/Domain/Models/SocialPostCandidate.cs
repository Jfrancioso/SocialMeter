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
    /// <summary>The platform's stable id for the author (Reddit t2_ fullname, X user id). Null when unavailable.</summary>
    string? AuthorId = null,
    /// <summary>When the author's account was created. Null when the source could not tell us.</summary>
    DateTime? AuthorCreatedAt = null,
    /// <summary>Reddit karma or X follower count — a rough "has this account participated" signal.</summary>
    int? AuthorReputation = null
);
