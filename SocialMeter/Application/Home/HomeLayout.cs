using SocialMeter.Contracts.Home;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Home;

/// <summary>The home page's row keys, as used in Home:Rows and on HomeRowDto.Key (the page uses them as anchors too).</summary>
public static class HomeRowKeys
{
    // List rows: titles from TMDB or Steam lists.
    public const string Trending = "trending";
    public const string NowPlaying = "now_playing";
    public const string Seasonal = "seasonal";
    public const string OnTheAir = "on_the_air";
    public const string TopGames = "top_games";
    public const string PopularMovies = "popular_movies";
    public const string PopularTv = "popular_tv";
    public const string MostPlayed = "most_played";
    public const string HorrorTrending = "horror_trending";
    public const string HorrorTheaters = "horror_theaters";

    // Signal rows: built from our own readings.
    public const string Gaps = "gaps";
    public const string HeatingUp = "heating_up";
    public const string CoolingOff = "cooling_off";
}

/// <summary>How rows share the page. Pure, so it is tested without TMDB or a database (HomeLayoutTests).</summary>
public static class HomeLayout
{
    /// <summary>Rows that say something about a title (it's climbing, the critics disagree) rather than list it: they keep every title and never take one from a list row.</summary>
    public static readonly IReadOnlySet<string> SignalRows = new HashSet<string> { HomeRowKeys.Gaps, HomeRowKeys.HeatingUp, HomeRowKeys.CoolingOff };

    /// <summary>
    /// Each title appears only in the first list row (in page order) that has it, so the page shows more titles and fewer
    /// repeats; signal rows are exempt. A row left with nothing is dropped.
    /// </summary>
    public static List<HomeRowDto> Dedupe(IEnumerable<HomeRowDto> rows)
    {
        var seen = new HashSet<(MediaType, int)>();
        var result = new List<HomeRowDto>();
        foreach (var row in rows)
        {
            var items = SignalRows.Contains(row.Key)
                ? row.Items.ToList()
                : row.Items.Where(t => seen.Add((t.MediaType, t.TmdbId))).ToList();
            if (items.Count > 0)
                result.Add(new HomeRowDto { Key = row.Key, Title = row.Title, Subtitle = row.Subtitle, Items = items });
        }
        return result;
    }
}

/// <summary>What the search box offers before anything is typed. Pure, so it is tested on its own.</summary>
public static class SearchSuggestions
{
    /// <summary>
    /// The titles people post about most, at most four movies and shows plus one game, busiest first. Steam reviews arrive
    /// by the hundred and would crowd out every movie and show, so games get one place.
    /// </summary>
    public static IReadOnlyList<TalkedAbout> Pick(IEnumerable<TalkedAbout> candidates)
    {
        var list = candidates.ToList();
        return list.Where(c => c.MediaType != MediaType.Game).Take(4)
            .Concat(list.Where(c => c.MediaType == MediaType.Game).Take(1))
            .OrderByDescending(c => c.Posts)
            .ToList();
    }
}
