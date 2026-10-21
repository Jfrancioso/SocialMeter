namespace SocialMeter.Application.Social;

/// <summary>
/// The statistics behind the meter, kept pure so every number on the "why this score" panel can be reproduced by hand.
///
///  - Weighted mean with Kish's effective sample size (how many equally-weighted posts the weighted set is worth).
///  - Standard error of a weighted mean under reliability weights, with a Student-t 95 % interval.
///  - Bayesian average (the formula IMDb publishes for its Top 250): the mean is pulled toward a neutral prior until
///    enough evidence accumulates to outweigh it.
///  - Mann-Kendall trend test with Sen's slope: the standard non-parametric test for a monotonic trend in a short series.
/// </summary>
public static class ScoreStatistics
{
    /// <summary>Summary of a weighted sample. StandardError is NaN when fewer than two effective observations exist.</summary>
    public readonly record struct WeightedSummary(double Mean, double SumWeights, double KishN, double StandardError)
    {
        public static readonly WeightedSummary Empty = new(0, 0, 0, double.NaN);
    }

    public static WeightedSummary Summarize(IReadOnlyList<(double Value, double Weight)> items)
    {
        double sw = 0, sw2 = 0, swx = 0;
        foreach (var (x, w) in items)
        {
            if (w <= 0) continue;
            sw += w;
            sw2 += w * w;
            swx += w * x;
        }
        if (sw <= 0) return WeightedSummary.Empty;

        var mean = swx / sw;
        var kish = sw * sw / sw2;

        // Var(weighted mean) = Σ w²(x−μ)² / (Σw)², with the usual n/(n−1) small-sample correction on the effective n.
        double s = 0;
        foreach (var (x, w) in items)
        {
            if (w <= 0) continue;
            s += w * w * (x - mean) * (x - mean);
        }
        var se = kish > 1 ? Math.Sqrt(s) / sw * Math.Sqrt(kish / (kish - 1)) : double.NaN;
        return new WeightedSummary(mean, sw, kish, se);
    }

    /// <summary>
    /// Half-width of the 95 % interval around the weighted mean, in the sample's own units (sentiment runs −1..1, so 1 means
    /// "could be anywhere"). <paramref name="minSigma"/> is the floor on the per-observation spread: ten posts that all read
    /// exactly +0.9 are not proof of zero uncertainty, because the reading itself has noise.
    /// </summary>
    public static double Margin95(WeightedSummary s, double minSigma)
    {
        if (s.KishN < 2 || double.IsNaN(s.StandardError)) return 1;
        var se = Math.Max(s.StandardError, minSigma / Math.Sqrt(s.KishN));
        return Math.Min(1, TCritical95(s.KishN - 1) * se);
    }

    /// <summary>Bayesian average: (n·mean + k·prior) / (n + k). With n = k the data and the prior count equally.</summary>
    public static double Shrink(double mean, double n, double prior, double k)
        => k <= 0 ? mean : (n * mean + k * prior) / (n + k);

    /// <summary>Two-sided 95 % Student-t critical value, linearly interpolated from the standard table.</summary>
    public static double TCritical95(double df)
    {
        if (df <= 1) return TTable[0].T;
        for (var i = 1; i < TTable.Length; i++)
        {
            if (df > TTable[i].Df) continue;
            var (d0, t0) = TTable[i - 1];
            var (d1, t1) = TTable[i];
            return t0 + (t1 - t0) * (df - d0) / (d1 - d0);
        }
        return 1.960;
    }

    private static readonly (double Df, double T)[] TTable =
    {
        (1, 12.706), (2, 4.303), (3, 3.182), (4, 2.776), (5, 2.571), (6, 2.447), (7, 2.365), (8, 2.306), (9, 2.262),
        (10, 2.228), (12, 2.179), (15, 2.131), (20, 2.086), (25, 2.060), (30, 2.042), (40, 2.021), (60, 2.000),
        (120, 1.980), (240, 1.970), (1000, 1.962)
    };

    // ---- Trend ----

    /// <summary>
    /// Direction is "rising" / "falling" when the Mann-Kendall p-value is below alpha, "steady" otherwise, and
    /// "insufficient" below the minimum number of points. Slope is Sen's estimator in value units per unit of time.
    /// </summary>
    public readonly record struct TrendResult(int Points, int S, double Z, double PValue, double Slope, string Direction)
    {
        public static TrendResult Insufficient(int points) => new(points, 0, 0, 1, 0, "insufficient");
    }

    /// <summary>Mann-Kendall test over (time, value) points in time order, with the tie correction on the variance.</summary>
    public static TrendResult MannKendall(IReadOnlyList<(double Time, double Value)> series, double alpha = 0.05, int minPoints = 5)
    {
        var n = series.Count;
        if (n < Math.Max(3, minPoints)) return TrendResult.Insufficient(n);

        var s = 0;
        var slopes = new List<double>(n * (n - 1) / 2);
        for (var i = 0; i < n - 1; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var dy = series[j].Value - series[i].Value;
                var dt = series[j].Time - series[i].Time;
                s += Math.Sign(dy);
                if (dt > 0) slopes.Add(dy / dt);
            }
        }

        double variance = n * (n - 1) * (2.0 * n + 5);
        foreach (var tie in series.GroupBy(p => p.Value).Select(g => g.Count()).Where(c => c > 1))
            variance -= tie * (tie - 1) * (2.0 * tie + 5);
        variance /= 18;

        var z = variance <= 0 || s == 0 ? 0 : (s - Math.Sign(s)) / Math.Sqrt(variance);
        var p = 2 * (1 - NormalCdf(Math.Abs(z)));

        slopes.Sort();
        var slope = slopes.Count == 0 ? 0 : slopes.Count % 2 == 1 ? slopes[slopes.Count / 2] : (slopes[slopes.Count / 2 - 1] + slopes[slopes.Count / 2]) / 2;

        var direction = p < alpha ? (s > 0 ? "rising" : "falling") : "steady";
        return new TrendResult(n, s, z, p, slope, direction);
    }

    /// <summary>Exponentially weighted moving average; alpha is the weight on the newest point.</summary>
    public static double[] Ewma(IReadOnlyList<double> series, double alpha)
    {
        var result = new double[series.Count];
        for (var i = 0; i < series.Count; i++)
            result[i] = i == 0 ? series[0] : alpha * series[i] + (1 - alpha) * result[i - 1];
        return result;
    }

    public static double NormalCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2));

    // Complementary error function, Numerical Recipes' Chebyshev fit (fractional error below 1.2e-7 everywhere).
    private static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + 0.5 * z);
        var r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 +
                t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? r : 2 - r;
    }
}
