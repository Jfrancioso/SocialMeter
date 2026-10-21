namespace SocialMeter.Contracts.Social;

public sealed class ScoreHistoryDto
{
    public IReadOnlyList<ScorePointDto> Points { get; init; } = Array.Empty<ScorePointDto>();
    public TrendDto Trend { get; init; } = new();
}

public sealed class ScorePointDto
{
    public DateOnly Day { get; init; }
    public int Score { get; init; }
    public int RawScore { get; init; }
    public int Margin { get; init; }
    public double EffectivePosts { get; init; }
    public int TotalPosts { get; init; }

    /// <summary>Exponentially smoothed score, for the line; the dots are the real readings.</summary>
    public double Smoothed { get; set; }

    /// <summary>Rebuilt from post dates rather than observed on the day.</summary>
    public bool Reconstructed { get; init; }
}

public sealed class TrendDto
{
    /// <summary>"rising" / "falling" / "steady" / "insufficient".</summary>
    public string Direction { get; init; } = "insufficient";

    /// <summary>Daily readings the test ran on.</summary>
    public int Points { get; init; }
    public int PointsNeeded { get; init; }
    public int WindowDays { get; init; }

    /// <summary>Mann-Kendall two-sided p-value; below the configured alpha the direction is called.</summary>
    public double PValue { get; init; } = 1;

    /// <summary>Sen's slope, points per day.</summary>
    public double SlopePerDay { get; init; }

    /// <summary>Last reading minus first reading in the window; null with fewer than two.</summary>
    public int? Change { get; init; }
}
