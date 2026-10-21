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
/// <param name="SensitiveMedia">The assessor judged an attached image unsuitable to show by default (nudity, gore, suggestive content).</param>
/// <param name="Substance">0–1: how much of the author's actual experience is in the post. "10/10" ≈ 0.1; a developed argument with specifics ≈ 0.9.</param>
public readonly record struct PostAssessment(bool AboutTitle, double Sentiment, string? Note, bool SensitiveMedia = false, double Substance = 0.5)
{
    public SentimentResult ToResult() => SentimentResult.FromScore(Sentiment);
}
