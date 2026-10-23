using SocialMeter.Application.Home;

namespace SocialMeter.Options;

/// <summary>
/// The home page's rows, in order, by key: a list of slots rather than code, so rows can be reordered or dropped (and,
/// later, a labelled sponsored row inserted) by changing configuration. Only listed rows are fetched; unknown keys are
/// skipped; a title appears only in the first row that has it, and a row left empty is hidden.
/// Keys: horror_trending and horror_theaters (October only), heating_up, cooling_off, gaps (crowd vs critics), trending,
/// now_playing, seasonal, on_the_air, top_games, popular_movies, popular_tv, most_played.
/// heating_up and cooling_off stay hidden until enough daily readings exist for the trend test.
/// </summary>
public sealed class HomeOptions
{
    public const string SectionName = "Home";

    /// <summary>Used when configuration lists no rows.</summary>
    public static readonly IReadOnlyList<string> DefaultRows = new[]
    {
        HomeRowKeys.HorrorTrending, HomeRowKeys.HeatingUp, HomeRowKeys.Gaps, HomeRowKeys.Trending,
        HomeRowKeys.NowPlaying, HomeRowKeys.Seasonal, HomeRowKeys.CoolingOff, HomeRowKeys.MostPlayed
    };

    // Empty by default on purpose: the configuration binder appends to an existing array or list rather than replacing
    // it, so a default here could never be shortened or reordered by a setting.
    public string[] Rows { get; set; } = Array.Empty<string>();

    /// <summary>The rows to show, in order, without repeats.</summary>
    public IReadOnlyList<string> Effective => (Rows.Length > 0 ? Rows : DefaultRows).Distinct().ToList();

    // ---- Signal rows (gaps, heating_up, cooling_off): which of our readings are solid enough to show. ----

    /// <summary>Crowd vs critics: counted posts a title needs before its crowd score is compared with the critics'.</summary>
    public int GapMinPosts { get; set; } = 15;

    /// <summary>Crowd vs critics: the reading's confidence (0-1, how much of it is posts rather than the pull toward 50).</summary>
    public double GapMinConfidence { get; set; } = 0.5;

    /// <summary>Crowd vs critics: the smallest gap, in points, worth a place in the row.</summary>
    public int GapMinPoints { get; set; } = 10;

    /// <summary>Heating up / cooling off: counted posts a daily reading needs to take part in the trend test.</summary>
    public int MoverMinPosts { get; set; } = 15;

    /// <summary>Titles per signal row.</summary>
    public int SignalRowSize { get; set; } = 12;

    /// <summary>
    /// How often HomeWarmupWorker rebuilds the cached home page and search suggestions, in minutes; 0 turns it off.
    /// Shorter than the five-minute cache, so a visitor never meets an expired one. TMDB calls only, no paid ones.
    /// </summary>
    public int WarmEveryMinutes { get; set; } = 4;
}
