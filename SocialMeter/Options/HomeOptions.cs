namespace SocialMeter.Options;

/// <summary>
/// The home page's rows, in order, by key: a list of slots rather than code, so rows can be reordered or dropped (and,
/// later, a labelled sponsored row inserted) by changing configuration. Only listed rows are fetched; unknown keys are
/// skipped; a title appears only in the first row that has it, and a row left empty is hidden.
/// Keys: horror_trending and horror_theaters (October only), trending, now_playing, seasonal, on_the_air, top_games,
/// popular_movies, popular_tv, most_played.
/// </summary>
public sealed class HomeOptions
{
    public const string SectionName = "Home";

    /// <summary>Used when configuration lists no rows.</summary>
    public static readonly IReadOnlyList<string> DefaultRows = new[] { "horror_trending", "trending", "now_playing", "seasonal", "most_played" };

    // Empty by default on purpose: the configuration binder appends to an existing array or list rather than replacing
    // it, so a default here could never be shortened or reordered by a setting.
    public string[] Rows { get; set; } = Array.Empty<string>();

    /// <summary>The rows to show, in order, without repeats.</summary>
    public IReadOnlyList<string> Effective => (Rows.Length > 0 ? Rows : DefaultRows).Distinct().ToList();
}
