namespace SocialMeter.Domain.Models;

/// <summary>One reading per title per day, so the meter has a trend and not just a number. Persisted.</summary>
public sealed class ScoreHistory
{
    public int Id { get; set; }

    public int TmdbId { get; set; }
    public MediaType MediaType { get; set; }

    /// <summary>UTC calendar day the reading describes.</summary>
    public DateOnly Day { get; set; }

    public int Score { get; set; }
    public int RawScore { get; set; }
    public double EffectivePosts { get; set; }
    public int Margin { get; set; }
    public int TotalPosts { get; set; }

    /// <summary>
    /// True when the row was rebuilt from stored post dates rather than observed on the day: engagement counts and
    /// trust verdicts are today's, so the reading is an estimate of what the meter would have said.
    /// </summary>
    public bool Reconstructed { get; set; }

    public DateTime RecordedAt { get; set; }
}
