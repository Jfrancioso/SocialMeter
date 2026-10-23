using SocialMeter.Application.Home;
using SocialMeter.Application.Ratings;
using SocialMeter.Domain.Models;
using Xunit;

namespace SocialMeter.Tests;

public class HomeSignalsTests
{
    // ---- Crowd vs critics ----

    private static CrowdCritics C(int id, int crowd, int critics, int posts = 50, double confidence = 0.8, MediaType type = MediaType.Movie)
        => new(type, id, crowd, critics, posts, confidence);

    [Fact]
    public void Biggest_disagreements_first_either_way()
    {
        var picks = HomeSignals.BiggestGaps(new[]
        {
            C(1, 79, 59),        // fans +20
            C(2, 40, 85),        // critics +45
            C(3, 70, 66),        // too close to call a gap
            C(4, 90, 50, posts: 5),          // too few posts
            C(5, 90, 50, confidence: 0.3),   // too unsure
            C(6, 90, 40, type: MediaType.Game)
        }, minPosts: 15, minConfidence: 0.5, minGap: 10, take: 12);

        Assert.Equal(new[] { 2, 1 }, picks.Select(p => p.TmdbId));
        Assert.Equal(-45, picks[0].Gap);
    }

    [Fact]
    public void Critics_means_tomatometer_and_metascore_not_audience_ratings()
    {
        var ratings = new[]
        {
            new ExternalRating { Source = "tmdb", Normalized = 88 },
            new ExternalRating { Source = "imdb", Normalized = 93 },
            new ExternalRating { Source = "rottenTomatoes", Normalized = 100 },
            new ExternalRating { Source = "metacritic", Normalized = 98 }
        };
        Assert.Equal((99, "critics"), CriticsBaseline.For(MediaType.Movie, ratings));
        Assert.Null(CriticsBaseline.For(MediaType.Tv, ratings.Take(2).ToList()).Score);   // audience ratings only: no critics score
        Assert.Equal((41, "Steam all-time"), CriticsBaseline.For(MediaType.Game, new[] { new ExternalRating { Source = "steam", Normalized = 41 } }));
    }

    // ---- Heating up / cooling off ----

    private static (MediaType, int, IReadOnlyList<(DateOnly, int)>) S(int id, params int[] scores)
        => (MediaType.Movie, id, scores.Select((s, i) => (new DateOnly(2026, 10, 23).AddDays(i), s)).ToList());

    [Fact]
    public void Steady_climbs_and_falls_are_movers_flat_and_short_series_are_not()
    {
        var (rising, falling) = HomeSignals.Movers(new[]
        {
            S(1, 60, 62, 64, 66, 68, 70, 72),       // climbing
            S(2, 70, 68, 66, 63, 61, 59, 57),       // slipping
            S(3, 65, 66, 64, 65, 66, 64, 65),       // flat
            S(4, 50, 60, 70, 80)                    // only four days: not enough to say
        }, alpha: 0.05, minPoints: 5, take: 12);

        Assert.Equal(new[] { 1 }, rising.Select(m => m.TmdbId));
        Assert.Equal(new[] { 2 }, falling.Select(m => m.TmdbId));
        Assert.True(rising[0].SlopePerDay > 0 && falling[0].SlopePerDay < 0);
    }
}
