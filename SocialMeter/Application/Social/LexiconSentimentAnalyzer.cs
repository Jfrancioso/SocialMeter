using System.Text.RegularExpressions;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>
/// Lexicon-based sentiment tuned for talk about movies and shows. Handles multi-word phrases ("movie of the year"),
/// numeric ratings ("9/10"), negation ("not good"), intensifiers ("absolutely stunning") and slang.
/// Deterministic and dependency-free; swap in an ML or LLM analyzer behind <see cref="ISentimentAnalyzer"/>
/// if you want finer grading.
/// </summary>
public sealed partial class LexiconSentimentAnalyzer : ISentimentAnalyzer
{
    // Phrases are matched before single words and removed from the text so their parts don't score twice.
    // Longest first so "best movie of the year" beats "of the year".
    private static readonly (string Phrase, double Weight)[] Phrases =
        new (string Phrase, double Weight)[]
        {
            ("best movie of the year", 3), ("best film of the year", 3), ("best show of the year", 3),
            ("movie of the year", 2.5), ("film of the year", 2.5), ("show of the year", 2.5),
            ("worst movie of the year", -3), ("worst film of the year", -3),
            ("movie of the decade", 3), ("one of the best", 2.5), ("one of the worst", -2.5),
            ("breath taking", 3), ("mind blowing", 2.5), ("jaw dropping", 2.5), ("top tier", 2.5),
            ("peak cinema", 3), ("peak television", 3), ("peak tv", 3), ("must watch", 2.5), ("must see", 2.5),
            ("so good", 2), ("so much better", 2), ("way better", 1.5), ("can't wait", 1.5), ("cant wait", 1.5),
            ("highly recommend", 2.5), ("worth watching", 1.5), ("worth it", 1.5), ("not bad", 1),
            ("slept on", 1.5), ("did not disappoint", 2), ("didn't disappoint", 2), ("didnt disappoint", 2),
            ("waste of time", -3), ("waste of money", -3), ("fell asleep", -2.5), ("walked out", -3),
            ("turned it off", -2.5), ("couldn't finish", -2.5), ("couldnt finish", -2.5), ("not worth", -2),
            ("let down", -2), ("skip it", -2), ("skip this", -2), ("so bad", -2.5), ("so boring", -3),
            ("dumpster fire", -3), ("hot garbage", -3), ("hate watch", -1.5), ("fell flat", -2), ("falls flat", -2),
            ("too long", -1), ("makes no sense", -2), ("made no sense", -2), ("plot holes", -1.5),
            ("cash grab", -2.5), ("didn't like", -2), ("didnt like", -2), ("did not like", -2),
            ("don't get the hype", -1.5), ("dont get the hype", -1.5), ("not for me", -1)
        }
        .OrderByDescending(p => p.Phrase.Length)
        .ToArray();

    private static readonly Dictionary<string, double> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // strongly positive
        ["masterpiece"] = 3, ["perfection"] = 3, ["flawless"] = 3, ["phenomenal"] = 3, ["incredible"] = 3,
        ["amazing"] = 3, ["stunning"] = 3, ["breathtaking"] = 3, ["brilliant"] = 3, ["outstanding"] = 3,
        ["spectacular"] = 3, ["goated"] = 3, ["peak"] = 2.5, ["chills"] = 2, ["banger"] = 2.5, ["stellar"] = 2.5,
        // positive
        ["love"] = 2, ["loved"] = 2, ["adore"] = 2, ["adored"] = 2, ["obsessed"] = 2, ["great"] = 2,
        ["fantastic"] = 2.5, ["excellent"] = 2.5, ["superb"] = 2.5, ["perfect"] = 2.5, ["best"] = 2,
        ["beautiful"] = 2, ["beautifully"] = 2, ["gorgeous"] = 2, ["wonderful"] = 2, ["awesome"] = 2,
        ["epic"] = 2, ["gripping"] = 2, ["thrilling"] = 2, ["engaging"] = 1.5, ["hilarious"] = 2, ["fun"] = 1.5,
        ["enjoyed"] = 1.5, ["enjoyable"] = 1.5, ["favorite"] = 2, ["favourite"] = 2, ["recommend"] = 1.5,
        ["impressed"] = 1.5, ["impressive"] = 1.5, ["worth"] = 1, ["good"] = 1.5, ["solid"] = 1.5, ["strong"] = 1,
        ["better"] = 1.5, ["nice"] = 1.5, ["nicest"] = 2, ["decent"] = 0.75, ["fine"] = 0.5, ["okay"] = 0.25,
        ["ok"] = 0.25, ["cool"] = 1, ["clever"] = 1.5, ["smart"] = 1, ["moving"] = 1.5, ["emotional"] = 1,
        ["powerful"] = 1.5, ["satisfying"] = 1.5, ["underrated"] = 1.5, ["rewatch"] = 1.5, ["rewatched"] = 1.5,
        ["cried"] = 1, ["laughed"] = 1.5, ["slaps"] = 2, ["fire"] = 2, ["win"] = 1, ["hit"] = 1, ["delight"] = 2,
        ["delightful"] = 2, ["charming"] = 1.5, ["fresh"] = 1, ["wholesome"] = 1.5, ["cute"] = 1.5, ["gem"] = 2,
        ["excited"] = 1.5, ["hyped"] = 1, ["stunned"] = 1.5, ["touching"] = 1.5, ["heartwarming"] = 2,
        ["thrilled"] = 2, ["recommended"] = 1.5, ["memorable"] = 1.5, ["iconic"] = 2, ["legendary"] = 2.5,
        // negative
        ["disappointing"] = -2, ["disappointed"] = -2, ["disappointment"] = -2, ["disappoint"] = -2,
        ["letdown"] = -2, ["boring"] = -2.5, ["bored"] = -2, ["bland"] = -2, ["dull"] = -2, ["tedious"] = -2,
        ["slow"] = -1, ["mess"] = -2, ["messy"] = -1.5, ["bad"] = -2, ["terrible"] = -3, ["awful"] = -3,
        ["horrible"] = -3, ["worst"] = -3, ["trash"] = -3, ["garbage"] = -3, ["hate"] = -2.5, ["hated"] = -2.5,
        ["waste"] = -2.5, ["wasted"] = -2, ["predictable"] = -1.5, ["cringe"] = -2, ["cringey"] = -2, ["flop"] = -2,
        ["overrated"] = -1.5, ["overhyped"] = -1.5, ["forgettable"] = -2, ["mediocre"] = -1.5, ["meh"] = -1,
        ["mid"] = -1.5, ["weak"] = -1.5, ["lazy"] = -2, ["generic"] = -1.5, ["underwhelming"] = -2,
        ["painful"] = -2, ["pointless"] = -2, ["ruined"] = -2.5, ["ruin"] = -2, ["snooze"] = -2, ["sucks"] = -2.5,
        ["sucked"] = -2.5, ["confusing"] = -1.5, ["lifeless"] = -2, ["annoying"] = -1.5, ["stupid"] = -2,
        ["dumb"] = -2, ["cheap"] = -1.5, ["flat"] = -1.5, ["fail"] = -2, ["failed"] = -2, ["failure"] = -2,
        ["skip"] = -1.5, ["unwatchable"] = -3, ["disaster"] = -3, ["butchered"] = -2.5, ["miss"] = -1,
        ["nonsense"] = -2, ["problem"] = -1, ["problems"] = -1, ["issue"] = -0.5, ["issues"] = -1, ["bloated"] = -1.5,
        ["drag"] = -1.5, ["dragged"] = -1.5, ["dragging"] = -1.5, ["wooden"] = -2, ["cheesy"] = -1, ["lame"] = -2,
        ["pissed"] = -2, ["angry"] = -1.5, ["frustrating"] = -2, ["frustrated"] = -1.5, ["regret"] = -2,
        ["ugly"] = -2, ["soulless"] = -2.5, ["hollow"] = -2, ["shallow"] = -1.5, ["clunky"] = -1.5,
        ["insufferable"] = -2.5, ["unbearable"] = -2.5, ["atrocious"] = -3, ["abysmal"] = -3, ["dreadful"] = -3
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
        ["pretty"] = 1.1, ["quite"] = 1.1, ["god"] = 1.3, ["damn"] = 1.3,
        ["kinda"] = 0.6, ["somewhat"] = 0.6, ["slightly"] = 0.5, ["bit"] = 0.6, ["little"] = 0.6, ["mildly"] = 0.5
    };

    public SentimentResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SentimentResult.FromScore(0);

        // Normalize: lower-case, drop links, turn hyphens into spaces so "mind-blowing" matches "mind blowing".
        var work = LinkRegex().Replace(text, " ").ToLowerInvariant().Replace('-', ' ');

        double sum = 0;
        var hits = 0;

        // 1. Explicit ratings like "9/10" or "4/5". Anything above the midpoint is praise.
        foreach (Match m in RatingRegex().Matches(work))
        {
            var n = double.Parse(m.Groups[1].Value);
            var outOf = double.Parse(m.Groups[2].Value);
            if (outOf is not (5 or 10) || n > outOf) continue;
            var centered = (n / outOf - 0.5) * 2; // -1..1
            sum += centered * 3;
            hits++;
        }
        work = RatingRegex().Replace(work, " ");

        // 2. Phrases.
        foreach (var (phrase, weight) in Phrases)
        {
            var idx = work.IndexOf(phrase, StringComparison.Ordinal);
            if (idx < 0) continue;

            var negated = IsNegatedBefore(work, idx);
            sum += negated ? -weight * 0.75 : weight;
            hits++;
            work = work.Replace(phrase, " ");
        }

        // 3. Single words with a three-word look-back for negation and intensity.
        var tokens = TokenRegex().Matches(work).Select(m => m.Value).ToArray();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!Lexicon.TryGetValue(tokens[i], out var weight))
                continue;

            hits++;
            var modifier = 1.0;
            var negated = false;

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

    private static bool IsNegatedBefore(string text, int index)
    {
        var start = Math.Max(0, index - 24);
        var before = TokenRegex().Matches(text[start..index]).Select(m => m.Value).TakeLast(3);
        return before.Any(Negators.Contains);
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z']*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"https?://\S+|t\.co/\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\b(\d{1,2}(?:\.\d)?)\s*/\s*(5|10)\b")]
    private static partial Regex RatingRegex();
}
