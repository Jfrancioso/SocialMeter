using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>
/// For posts that carry an explicit platform verdict (Steam's thumbs up/down) no model needs to guess: the verdict
/// sets the direction, and the lexicon only shades how strongly the text feels. Free, deterministic, always on-topic.
/// </summary>
public sealed class VerdictAssessor
{
    public const string AnalyzerName = "verdict";

    private readonly ISentimentAnalyzer _lexicon;

    public VerdictAssessor(ISentimentAnalyzer lexicon)
    {
        _lexicon = lexicon;
    }

    public PostAssessment Assess(SocialPostCandidate c)
    {
        var direction = c.VerdictUp == true ? 1.0 : -1.0;
        var tone = _lexicon.Analyze(c.Content).Score; // -1..1

        // Base strength from the verdict; the text can push it further in the same direction or soften it a little.
        var agrees = Math.Sign(tone) == Math.Sign(direction);
        var strength = 0.6 + (agrees ? 0.4 * Math.Abs(tone) : -0.25 * Math.Abs(tone));
        var score = direction * Math.Clamp(strength, 0.25, 1.0);

        var note = c.VerdictUp == true ? "Recommended on Steam" : "Not recommended on Steam";
        return new PostAssessment(true, score, note);
    }
}
