using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>
/// The no-credentials assessor: scores tone with the word lexicon and trusts the source's title filter for relevance.
/// It cannot tell "Grok sucks, this is Hill House" from "Hill House sucks" — that is what <c>ClaudePostAssessor</c> is for.
/// </summary>
public sealed class LexiconPostAssessor : IPostAssessor
{
    private readonly ISentimentAnalyzer _sentiment;

    public LexiconPostAssessor(ISentimentAnalyzer sentiment)
    {
        _sentiment = sentiment;
    }

    public string Name => "lexicon";

    public Task<IReadOnlyList<PostAssessment>> AssessAsync(Title title, IReadOnlyList<SocialPostCandidate> posts, CancellationToken ct = default)
    {
        var results = posts
            .Select(p => new PostAssessment(true, _sentiment.Analyze(p.Content).Score, null, false, SubstanceHeuristic.Score(p.Content)))
            .ToList();
        return Task.FromResult<IReadOnlyList<PostAssessment>>(results);
    }
}
