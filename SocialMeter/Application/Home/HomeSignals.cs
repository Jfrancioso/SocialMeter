using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Home;

/// <summary>A scored title with the crowd's score and the critics' (see CriticsBaseline).</summary>
public sealed record CrowdCritics(MediaType MediaType, int TmdbId, int Crowd, int Critics, int TotalPosts, double Confidence)
{
    public int Gap => Crowd - Critics;
}

/// <summary>A title whose daily score is trending, with the trend's slope in points per day.</summary>
public sealed record Mover(MediaType MediaType, int TmdbId, double SlopePerDay);

/// <summary>The selections behind the home page's "Crowd vs critics", "Heating up" and "Cooling off" rows.</summary>
public static class HomeSignals
{
    /// <summary>
    /// Where the crowd and the critics disagree most, either way, among readings solid enough to trust: enough posts, enough
    /// confidence, and a gap worth pointing at.
    /// </summary>
    public static IReadOnlyList<CrowdCritics> BiggestGaps(IEnumerable<CrowdCritics> titles, int minPosts, double minConfidence, int minGap, int take) =>
        titles
            .Where(t => t.MediaType != MediaType.Game && t.TotalPosts >= minPosts && t.Confidence >= minConfidence && Math.Abs(t.Gap) >= minGap)
            .OrderByDescending(t => Math.Abs(t.Gap))
            .ThenByDescending(t => t.TotalPosts)
            .Take(take)
            .ToList();

    /// <summary>
    /// Titles whose daily scores pass the same trend test the title page uses (Mann-Kendall at <paramref name="alpha"/>,
    /// at least <paramref name="minPoints"/> days), split into rising (steepest first) and falling (steepest first).
    /// </summary>
    public static (IReadOnlyList<Mover> Rising, IReadOnlyList<Mover> Falling) Movers(
        IEnumerable<(MediaType MediaType, int TmdbId, IReadOnlyList<(DateOnly Day, int Score)> Days)> series, double alpha, int minPoints, int take)
    {
        var rising = new List<Mover>();
        var falling = new List<Mover>();
        foreach (var s in series)
        {
            var points = s.Days.OrderBy(d => d.Day).Select(d => ((double)d.Day.DayNumber, (double)d.Score)).ToList();
            var test = ScoreStatistics.MannKendall(points, alpha, minPoints);
            if (test.Direction == "rising") rising.Add(new Mover(s.MediaType, s.TmdbId, test.Slope));
            else if (test.Direction == "falling") falling.Add(new Mover(s.MediaType, s.TmdbId, test.Slope));
        }
        return (rising.OrderByDescending(m => m.SlopePerDay).Take(take).ToList(),
                falling.OrderBy(m => m.SlopePerDay).Take(take).ToList());
    }
}
