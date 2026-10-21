using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

/// <summary>
/// Reads posts in the context of the title they were found for and decides two things per post:
/// whether it is actually about that title, and how the author feels about the title itself.
/// </summary>
public interface IPostAssessor
{
    /// <summary>Stored on each post so a later, better assessor can tell which posts it has not seen yet.</summary>
    string Name { get; }

    /// <summary>Returns one assessment per candidate, in the same order.</summary>
    Task<IReadOnlyList<PostAssessment>> AssessAsync(Title title, IReadOnlyList<SocialPostCandidate> posts, CancellationToken ct = default);
}

/// <param name="AboutTitle">False when the post only mentions the title in passing, is about a different work, or is news/promo rather than a reaction.</param>
/// <param name="Sentiment">-1..1 toward the title itself, ignoring feelings about anything else in the post.</param>
/// <param name="Note">Short reason, kept for auditing; never shown to readers.</param>
public readonly record struct PostAssessment(bool AboutTitle, double Sentiment, string? Note)
{
    public SentimentResult ToResult() => SentimentResult.FromScore(Sentiment);
}
