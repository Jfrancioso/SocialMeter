using SocialMeter.Application.Social;
using Xunit;

namespace SocialMeter.Tests;

public class ScoreStatisticsTests
{
    private static List<(double, double)> Items(params (double v, double w)[] xs) => xs.ToList();

    [Fact]
    public void Kish_effective_size_equals_n_for_equal_weights()
    {
        var s = ScoreStatistics.Summarize(Items((0.5, 2), (0.7, 2), (0.1, 2), (0.9, 2)));
        Assert.Equal(4, s.KishN, 6);
        Assert.Equal(0.55, s.Mean, 6);
    }

    [Fact]
    public void One_dominant_weight_collapses_the_effective_size_toward_one()
    {
        var s = ScoreStatistics.Summarize(Items((0.9, 100), (0.1, 1), (0.1, 1), (0.1, 1)));
        Assert.True(s.KishN < 1.1, $"KishN was {s.KishN}");
    }

    [Fact]
    public void Bayesian_average_meets_the_prior_halfway_at_n_equals_k()
    {
        Assert.Equal(0.45, ScoreStatistics.Shrink(0.9, n: 6, prior: 0, k: 6), 6);
        Assert.Equal(0.9, ScoreStatistics.Shrink(0.9, n: 6, prior: 0, k: 0), 6);
        Assert.True(ScoreStatistics.Shrink(0.9, n: 60, prior: 0, k: 6) > 0.8);
    }

    [Fact]
    public void Margin_narrows_with_more_posts_and_is_total_for_one()
    {
        var one = ScoreStatistics.Margin95(ScoreStatistics.Summarize(Items((0.8, 1))), 0.2);
        var five = ScoreStatistics.Margin95(ScoreStatistics.Summarize(Enumerable.Range(0, 5).Select(i => (0.6 + 0.1 * (i % 3), 1.0)).ToList()), 0.2);
        var fifty = ScoreStatistics.Margin95(ScoreStatistics.Summarize(Enumerable.Range(0, 50).Select(i => (0.6 + 0.1 * (i % 3), 1.0)).ToList()), 0.2);
        Assert.Equal(1, one);
        Assert.True(five > fifty, $"five={five} fifty={fifty}");
        Assert.True(fifty < 0.1, $"fifty={fifty}");
    }

    [Fact]
    public void Unanimous_posts_never_claim_zero_uncertainty()
    {
        var s = ScoreStatistics.Summarize(Enumerable.Range(0, 10).Select(_ => (0.9, 1.0)).ToList());
        // t(9) = 2.262, floor sigma 0.2 / sqrt(10)
        Assert.Equal(2.262 * 0.2 / Math.Sqrt(10), ScoreStatistics.Margin95(s, 0.2), 3);
    }

    [Theory]
    [InlineData(1, 12.706)]
    [InlineData(5, 2.571)]
    [InlineData(11, 2.2035)]
    [InlineData(1_000_000, 1.960)]
    public void T_critical_values_follow_the_table(double df, double expected)
    {
        Assert.Equal(expected, ScoreStatistics.TCritical95(df), 3);
    }

    [Fact]
    public void Normal_cdf_matches_known_values()
    {
        Assert.Equal(0.5, ScoreStatistics.NormalCdf(0), 6);
        Assert.Equal(0.975, ScoreStatistics.NormalCdf(1.96), 3);
        Assert.Equal(0.025, ScoreStatistics.NormalCdf(-1.96), 3);
    }

    [Fact]
    public void Mann_kendall_calls_a_steady_climb_rising()
    {
        var series = Enumerable.Range(0, 10).Select(i => ((double)i, 50.0 + 2 * i)).ToList();
        var t = ScoreStatistics.MannKendall(series);
        Assert.Equal("rising", t.Direction);
        Assert.Equal(45, t.S);
        Assert.True(t.PValue < 0.01);
        Assert.Equal(2, t.Slope, 6);
    }

    [Fact]
    public void Mann_kendall_calls_noise_steady_and_a_flat_line_steady()
    {
        var flat = Enumerable.Range(0, 10).Select(i => ((double)i, 60.0)).ToList();
        var noise = new double[] { 60, 58, 61, 59, 60, 62, 58, 60, 61, 59 }.Select((v, i) => ((double)i, v)).ToList();
        Assert.Equal("steady", ScoreStatistics.MannKendall(flat).Direction);
        Assert.Equal(0, ScoreStatistics.MannKendall(flat).S);
        Assert.Equal("steady", ScoreStatistics.MannKendall(noise).Direction);
    }

    [Fact]
    public void Mann_kendall_needs_a_minimum_number_of_points()
    {
        var t = ScoreStatistics.MannKendall(new[] { (0.0, 50.0), (1.0, 60.0), (2.0, 70.0) }, minPoints: 5);
        Assert.Equal("insufficient", t.Direction);
        Assert.Equal(3, t.Points);
    }

    [Fact]
    public void Sen_slope_uses_real_time_gaps()
    {
        // Readings on days 0, 2, 4, 6, 8 climbing 4 points per reading = 2 points per day.
        var series = Enumerable.Range(0, 5).Select(i => (2.0 * i, 50.0 + 4 * i)).ToList();
        Assert.Equal(2, ScoreStatistics.MannKendall(series).Slope, 6);
    }

    [Fact]
    public void Ewma_starts_at_the_first_point_and_lags_the_series()
    {
        var s = ScoreStatistics.Ewma(new double[] { 50, 70, 70, 70 }, 0.5);
        Assert.Equal(50, s[0]);
        Assert.Equal(60, s[1]);
        Assert.True(s[3] < 70 && s[3] > s[2]);
    }
}
