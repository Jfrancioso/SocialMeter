namespace SocialMeter.Domain.Models;

public static class TitleSnapshotExtensions
{
    /// <summary>Copies a computed reading onto the cached snapshot.</summary>
    public static void ApplyMeter(this TitleSnapshot snapshot, SocialMeterScore meter, DateTime refreshedAt)
    {
        snapshot.Score = meter.Score;
        snapshot.Label = meter.Label;
        snapshot.Confidence = meter.Confidence;
        snapshot.TotalPosts = meter.TotalPosts;
        snapshot.FilteredPosts = meter.FilteredPosts;
        snapshot.Positive = meter.Breakdown.Positive;
        snapshot.Neutral = meter.Breakdown.Neutral;
        snapshot.Negative = meter.Breakdown.Negative;
        snapshot.RawScore = meter.Stats.RawScore;
        snapshot.EffectivePosts = meter.Stats.EffectivePosts;
        snapshot.Margin = meter.Stats.Margin;
        snapshot.LastRefreshedAt = refreshedAt;
    }
}
