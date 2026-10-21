using System.Text.RegularExpressions;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>
/// Lexicon-based sentiment tuned for talk about movies and shows. Handles negation ("not good"),
/// intensifiers ("absolutely stunning") and a few slang terms. Deterministic and dependency-free;
/// swap in an ML or LLM analyzer behind <see cref="ISentimentAnalyzer"/> if you want finer grading.
/// </summary>
public sealed partial class LexiconSentimentAnalyzer : ISentimentAnalyzer
{
    private static readonly Dictionary<string, double> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // strongly positive
        ["masterpiece"] = 3, ["perfection"] = 3, ["flawless"] = 3, ["phenomenal"] = 3, ["incredible"] = 3,
        ["amazing"] = 3, ["stunning"] = 3, ["breathtaking"] = 3, ["brilliant"] = 3, ["outstanding"] = 3,
        ["spectacular"] = 3, ["goated"] = 3, ["peak"] = 2.5, ["chills"] = 2, ["banger"] = 2.5,
        // positive
        ["love"] = 2, ["loved"] = 2, ["great"] = 2, ["fantastic"] = 2.5, ["excellent"] = 2.5, ["superb"] = 2.5,
        ["perfect"] = 2.5, ["best"] = 2, ["beautiful"] = 2, ["gorgeous"] = 2, ["wonderful"] = 2, ["awesome"] = 2,
        ["epic"] = 2, ["gripping"] = 2, ["thrilling"] = 2, ["engaging"] = 1.5, ["hilarious"] = 2, ["fun"] = 1.5,
        ["enjoyed"] = 1.5, ["enjoyable"] = 1.5, ["favorite"] = 2, ["favourite"] = 2, ["recommend"] = 1.5,
        ["impressed"] = 1.5, ["impressive"] = 1.5, ["worth"] = 1, ["good"] = 1.5, ["solid"] = 1.5, ["strong"] = 1,
        ["decent"] = 0.75, ["fine"] = 0.5, ["okay"] = 0.25, ["ok"] = 0.25, ["cool"] = 1, ["clever"] = 1.5,
        ["smart"] = 1, ["moving"] = 1.5, ["emotional"] = 1, ["powerful"] = 1.5, ["satisfying"] = 1.5,
        ["underrated"] = 1.5, ["rewatch"] = 1.5, ["cried"] = 1, ["laughed"] = 1.5, ["slaps"] = 2, ["fire"] = 2,
        ["win"] = 1, ["hit"] = 1, ["delight"] = 2, ["delightful"] = 2, ["charming"] = 1.5, ["fresh"] = 1,
        // negative
        ["disappointing"] = -2, ["disappointed"] = -2, ["disappointment"] = -2, ["letdown"] = -2, ["boring"] = -2.5,
        ["bored"] = -2, ["bland"] = -2, ["dull"] = -2, ["tedious"] = -2, ["slow"] = -1, ["mess"] = -2, ["messy"] = -1.5,
        ["bad"] = -2, ["terrible"] = -3, ["awful"] = -3, ["horrible"] = -3, ["worst"] = -3, ["trash"] = -3,
        ["garbage"] = -3, ["hate"] = -2.5, ["hated"] = -2.5, ["waste"] = -2.5, ["wasted"] = -2, ["predictable"] = -1.5,
        ["cringe"] = -2, ["cringey"] = -2, ["flop"] = -2, ["overrated"] = -1.5, ["forgettable"] = -2, ["mediocre"] = -1.5,
        ["meh"] = -1, ["mid"] = -1.5, ["weak"] = -1.5, ["lazy"] = -2, ["generic"] = -1.5, ["underwhelming"] = -2,
        ["painful"] = -2, ["pointless"] = -2, ["ruined"] = -2.5, ["ruin"] = -2, ["snooze"] = -2, ["sucks"] = -2.5,
        ["sucked"] = -2.5, ["confusing"] = -1.5, ["lifeless"] = -2, ["annoying"] = -1.5, ["stupid"] = -2,
        ["dumb"] = -2, ["cheap"] = -1.5, ["flat"] = -1.5, ["fail"] = -2, ["failed"] = -2, ["failure"] = -2,
        ["skip"] = -1.5, ["unwatchable"] = -3, ["disaster"] = -3, ["butchered"] = -2.5, ["miss"] = -1,
        ["nonsense"] = -2, ["problem"] = -1, ["problems"] = -1, ["issue"] = -0.5, ["issues"] = -1, ["bloated"] = -1.5,
        ["drag"] = -1.5, ["dragged"] = -1.5, ["dragging"] = -1.5, ["wooden"] = -2, ["cheesy"] = -1, ["lame"] = -2
    };

    private static readonly HashSet<string> Negators = new(StringComparer.OrdinalIgnoreCase)
    {
        "not", "no", "never", "hardly", "barely", "isn't", "isnt", "wasn't", "wasnt", "aren't", "arent", "weren't",
        "werent", "don't", "dont", "didn't", "didnt", "doesn't", "doesnt", "can't", "cant", "couldn't", "couldnt",
        "won't", "wont", "wouldn't", "wouldnt", "ain't", "aint", "nothing", "neither", "nor", "without", "lacks", "lacking"
    };

    private static readonly Dictionary<string, double> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["very"] = 1.3, ["so"] = 1.3, ["really"] = 1.3, ["absolutely"] = 1.6, ["incredibly"] = 1.6, ["totally"] = 1.4,
        ["extremely"] = 1.6, ["insanely"] = 1.6, ["truly"] = 1.3, ["genuinely"] = 1.3, ["super"] = 1.4, ["utterly"] = 1.6,
        ["pretty"] = 1.1, ["quite"] = 1.1,
        ["kinda"] = 0.6, ["somewhat"] = 0.6, ["slightly"] = 0.5, ["bit"] = 0.6, ["little"] = 0.6, ["mildly"] = 0.5
    };

    public SentimentResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SentimentResult.FromScore(0);

        var tokens = TokenRegex().Matches(text).Select(m => m.Value).ToArray();
        if (tokens.Length == 0)
            return SentimentResult.FromScore(0);

        double sum = 0;
        var hits = 0;

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!Lexicon.TryGetValue(tokens[i], out var weight))
                continue;

            hits++;
            var modifier = 1.0;
            var negated = false;

            // Look back up to three words for "not", "really", "a bit" and the like.
            for (var j = Math.Max(0, i - 3); j < i; j++)
            {
                if (Negators.Contains(tokens[j])) negated = true;
                else if (Modifiers.TryGetValue(tokens[j], out var m)) modifier *= m;
            }

            var value = weight * modifier;
            if (negated) value = -value * 0.75;
            sum += value;
        }

        if (hits == 0)
            return SentimentResult.FromScore(0);

        // Exclamation marks are an intensity signal in casual posts; cap the boost.
        var bangs = Math.Min(text.Count(c => c == '!'), 3);
        sum *= 1 + bangs * 0.08;

        // Average the hits, then squash so one strong word reads as a clear lean, not a saturated score.
        var mean = sum / hits;
        var score = Math.Tanh(mean / 2.2);
        return SentimentResult.FromScore(score);
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z']*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
