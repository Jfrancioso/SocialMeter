using Microsoft.Extensions.Options;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// What the social sources search for one title. Most names are distinctive enough on their own ("Clayface", "The
/// Terminal List"); for a name that is an everyday word ("FROM", "Him", "Us") a post must also carry one of
/// <paramref name="Context"/> (cast, characters, creators, per-title extras), or the search returns sentences that only
/// contain the word and every one of them is paid for and thrown away.
/// </summary>
public sealed record TitleSearch(string Name, bool Ambiguous, IReadOnlyList<string> Context)
{
    /// <summary>An ambiguous name with nothing to pin it down: searching would only pay for noise, so the source skips it.</summary>
    public bool Skip => Ambiguous && Context.Count == 0;
}

/// <summary>How a title is searched. Pure, so it is tested without the network (TitleSearchTests).</summary>
public static class TitleSearchRules
{
    /// <summary>Context phrases kept per title; enough to catch the conversation without bloating the query.</summary>
    public const int MaxContext = 10;

    public static TitleSearch For(Title title, TitleSearchTuning? tuning)
    {
        var ambiguous = tuning?.Ambiguous ?? IsAmbiguous(title.Name);
        if (!ambiguous) return new TitleSearch(title.Name, false, Array.Empty<string>());

        // Hand-picked terms first: they are the most telling ("Fromville"), so they survive any trimming.
        var context = (tuning?.Terms ?? new List<string>())
            .Concat(ContextFor(title))
            .Select(t => t.Trim())
            .Where(t => t.Length >= 3 && !t.Equals(title.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxContext)
            .ToList();
        return new TitleSearch(title.Name, true, context);
    }

    /// <summary>
    /// A name is ambiguous when it is one everyday word, or two or three letters: "FROM", "Him", "Us", "It", "Smile".
    /// Anything longer, "The Boys" included, is specific enough that the media words already used do the job; requiring
    /// a cast name there would lose the plain "The Boys season 5 was great" posts.
    /// </summary>
    public static bool IsAmbiguous(string name)
    {
        var words = name.Split(new[] { ' ', '-', ':', '.', ',', '!', '?', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1 && (words[0].Length <= 3 || CommonWords.Contains(words[0].ToLowerInvariant()));
    }

    /// <summary>
    /// What people who talk about a title tend to name: its creators or directors, the top-billed cast, the main
    /// characters, and for games the developer. Networks and studios are left out on purpose: "MGM+" also matches every
    /// Amazon MGM Studios box-office post (measured on Bluesky, 2026-10-23).
    /// </summary>
    public static IReadOnlyList<string> ContextFor(Title title)
    {
        var terms = new List<string>();
        terms.AddRange(title.Creators);
        terms.AddRange(title.Cast.Take(4).Select(c => c.Name));
        terms.AddRange(title.Cast.Take(3).Select(c => c.Character).OfType<string>().Where(IsTellingCharacter));
        if (title.Developer is { Length: > 0 } developer) terms.Add(developer);
        return terms;
    }

    /// <summary>"Boyd Stevens" is telling; "Himself", "Mother" or "Kid" is not.</summary>
    private static bool IsTellingCharacter(string character)
    {
        var words = character.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || character.Contains("(voice)", StringComparison.OrdinalIgnoreCase)) return false;
        if (words.Length == 1) return words[0].Length >= 4 && !CommonWords.Contains(words[0].ToLowerInvariant()) && words[0] is not ("Himself" or "Herself");
        return true;
    }

    /// <summary>
    /// Everyday English words that are also titles often enough to matter. A single-word name in this list is searched
    /// with context. Add a word when a title's searches come back full of unrelated sentences.
    /// </summary>
    private static readonly HashSet<string> CommonWords = new(
        """
        about above across after again against all almost alone along also always among and another any anything
        around away back bad because become been before behind being below best better between beyond big black blue
        body both boy boys break bring brother but call came can care change child children city close cold come could
        country dark daughter day days dead death did different does done down dream each early earth end enough even
        ever every everything eye eyes face fall family far fast father fear feel few find fire first five follow for
        found four free friend friends from full game get girl girls give glass go god gold gone good got great green
        ground grow had half hand happy hard has have he head hear heart heat hell help her here high him his hold home
        hope hot hour house how human hunt idea into its just keep kid kids kill kind king know land last late leave
        left less let life light like line little live long look lost love low made make man many may me men might mine
        money more morning most mother move much must my name near never new next nice night nine no none nope not
        nothing now number off old once one only open or order other our out over own paper part past people perfect
        place play point power pretty quiet ready real red rest right river road room run said same saw say sea second
        see seen send seven shadow she should show side sign signs silence sister six sky small smile so some someone
        something son sound speak split spring stand star start still stop story strange summer sun sure take talk tell
        ten than that the their them then there these they thing things think this those three through time to today
        together told tomorrow too took top toward town tree true try turn two under until up upon very wait walk want
        war was watch water way we weapons were what when where which while white who whole why wild will wind winter
        with within without woman women word work world would year years yes yesterday yet you young your
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Builds a <see cref="TitleSearch"/> for a title, with any per-title tuning from configuration (Search:Titles).</summary>
public sealed class TitleSearchPlanner
{
    private readonly IOptionsMonitor<SearchOptions> _options;

    public TitleSearchPlanner(IOptionsMonitor<SearchOptions> options) => _options = options;

    public TitleSearch For(Title title)
    {
        var key = $"{title.MediaType.ToString().ToLowerInvariant()}_{title.TmdbId}";
        return TitleSearchRules.For(title, _options.CurrentValue.Titles.GetValueOrDefault(key));
    }
}
