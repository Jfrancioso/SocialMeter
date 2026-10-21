using System.Text.RegularExpressions;

namespace SocialMeter.Application.Social;

/// <summary>
/// How much of the author's actual experience is in a post, 0–1, without a model: length, specific references
/// (episodes, mechanics, characters, numbers) and the kind of words people use when they're describing rather than
/// just reacting. "10/10" scores near 0; a paragraph naming the third act and two characters scores near 1.
/// Used for Steam verdict reviews and the lexicon fallback; Claude returns its own substance for free-text posts.
/// </summary>
public static partial class SubstanceHeuristic
{
    private static readonly HashSet<string> SpecificWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // film / tv
        "act", "plot", "character", "characters", "ending", "finale", "episode", "episodes", "season", "scene", "scenes",
        "pacing", "dialogue", "script", "writing", "acting", "performance", "performances", "cinematography", "soundtrack",
        "score", "visuals", "effects", "cgi", "twist", "arc", "villain", "protagonist", "cast", "director", "sequel", "original",
        // games
        "gameplay", "mechanics", "mechanic", "combat", "balance", "balancing", "patch", "update", "server", "servers",
        "graphics", "story", "quest", "quests", "boss", "level", "levels", "loot", "progression", "grind", "fps", "bug", "bugs",
        "cheaters", "matchmaking", "netcode", "controls", "difficulty", "crafting", "pvp", "pve", "campaign", "multiplayer",
        "hours", "hour", "playtime", "optimization", "performance", "devs", "developers"
    };

    public static double Score(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;

        var words = TrustPolicy.Normalize(content).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;

        var lengthScore = Math.Min(1, words.Length / 60.0);

        var hits = words.Count(SpecificWords.Contains)
                   + Math.Min(2, NumberRegex().Matches(content).Count)
                   + Math.Min(2, ProperNounRegex().Matches(content).Count);
        var specificity = Math.Min(1, hits / 4.0);

        var sentences = Math.Min(1, SentenceRegex().Matches(content).Count / 4.0);

        return Math.Round(Math.Clamp(0.12 + 0.45 * lengthScore + 0.33 * specificity + 0.10 * sentences, 0, 1), 2);
    }

    // A number attached to a thing ("6 seconds", "40 hours", "35 levels") is a specific; "10/10" or "<3" is not.
    [GeneratedRegex(@"\b\d+(?:\.\d+)?\s+[A-Za-z]{2,}")]
    private static partial Regex NumberRegex();

    // Two or more capitalised words in a row, mid-sentence: names of people, places, episodes.
    [GeneratedRegex(@"(?<=[a-z,;:]\s)(?:[A-Z][a-z]+\s){1,3}[A-Z][a-z]+")]
    private static partial Regex ProperNounRegex();

    [GeneratedRegex(@"[.!?]+(?:\s|$)")]
    private static partial Regex SentenceRegex();
}
