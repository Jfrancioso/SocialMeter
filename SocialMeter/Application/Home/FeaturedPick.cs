using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Home;

/// <summary>A title and how many counted posts it drew in the featured window, with the confidence of its reading.</summary>
public sealed record TalkedAbout(MediaType MediaType, int TmdbId, int Posts, double Confidence);

/// <summary>
/// Which title leads the home page. Chatterbub measures conversation, so the hero is the tracked movie or show with the
/// most counted posts in the last few days, provided its reading is solid enough to put on the front page. Games are
/// left out because Steam reviews arrive in bulk and would always win, and their art doesn't fit the hero.
/// </summary>
public static class FeaturedPick
{
    public const string MostTalkedAbout = "Most talked about";
    public const string TrendingOnTmdb = "Trending on TMDB";

    /// <summary>Why the hero leads, for the page's wording (HomeResponseDto.FeaturedKind).</summary>
    public const string KindTalkedAbout = "talked-about", KindTrending = "trending", KindEvent = "event";

    /// <summary>Eligible candidates, most posts first; the caller takes the first one it can show (it needs a backdrop).</summary>
    public static IEnumerable<TalkedAbout> Eligible(IEnumerable<TalkedAbout> candidates, Func<TalkedAbout, bool> isTracked, int minPosts, double minConfidence)
        => candidates
            .Where(c => c.MediaType != MediaType.Game)
            .Where(c => c.Posts >= minPosts && c.Confidence >= minConfidence)
            .Where(isTracked)
            .OrderByDescending(c => c.Posts)
            .ThenByDescending(c => c.Confidence);
}
