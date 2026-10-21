using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>
/// Turns a set of analyzed posts into one SocialMeter reading. Pure and deterministic.
/// Each post's sentiment is weighted by community engagement (log-scaled so one viral post can't swamp the rest),
/// by recency, and by the trust the policy assigned to its author, then mapped to 0–100 where 50 is perfectly mixed.
/// Excluded posts never take part.
/// </summary>
public static class SocialScoreCalculator
{
    private const int FullConfidencePosts = 40;

    public static SocialMeterScore Compute(IReadOnlyCollection<SocialPost> allPosts, DateTime nowUtc, int momentumWindowDays)
    {
        var posts = allPosts.Where(p => !p.Excluded).ToList();
        var filtered = allPosts.Count - posts.Count;

        if (posts.Count == 0)
        {
            return new SocialMeterScore
            {
                Score = 50,
                Label = "No signal",
                Confidence = 0,
                ConfidenceLabel = "low",
                TotalPosts = 0,
                FilteredPosts = filtered
            };
        }

        var score = WeightedScore(posts, nowUtc);

        // Confidence grows with the trust-weighted volume, not the raw count, so a flood of weak posts stays "low".
        var effective = posts.Sum(p => p.TrustWeight);
        var confidence = Math.Round(Math.Min(1, effective / FullConfidencePosts), 2);

        var recent = posts.Where(p => (nowUtc - p.CreatedAt).TotalDays <= momentumWindowDays).ToList();
        int? momentum = recent.Count >= 3 && recent.Count < posts.Count
            ? WeightedScore(recent, nowUtc) - score
            : null;

        var platforms = posts
            .GroupBy(p => p.Platform)
            .Select(g => new PlatformSummary { Platform = g.Key, Posts = g.Count(), Score = WeightedScore(g.ToList(), nowUtc) })
            .OrderByDescending(p => p.Posts)
            .ToList();

        return new SocialMeterScore
        {
            Score = score,
            Label = SocialMeterScore.LabelFor(score),
            Confidence = confidence,
            ConfidenceLabel = SocialMeterScore.ConfidenceLabelFor(confidence),
            TotalPosts = posts.Count,
            FilteredPosts = filtered,
            DetailedPosts = posts.Count(p => p.Substance >= 0.6),
            Breakdown = new SentimentBreakdown
            {
                Positive = posts.Count(p => p.Sentiment == Sentiment.Positive),
                Neutral = posts.Count(p => p.Sentiment == Sentiment.Neutral),
                Negative = posts.Count(p => p.Sentiment == Sentiment.Negative)
            },
            Momentum = momentum,
            Platforms = platforms
        };
    }

    /// <summary>Community engagement, log-scaled. Used for ranking and for the "which of an account's posts counts" rule.</summary>
    public static double Engagement(SocialPost post)
        => 1 + 0.5 * Math.Log(1 + Math.Max(0, post.Upvotes)) + 0.25 * Math.Log(1 + Math.Max(0, post.Comments));

    /// <summary>How much a post pulls the needle: engagement × recency × trust × substance. A bare "10/10" counts half as much as a detailed take.</summary>
    public static double Weight(SocialPost post, DateTime nowUtc)
    {
        var ageDays = Math.Max(0, (nowUtc - post.CreatedAt).TotalDays);
        var recency = Math.Max(0.3, Math.Exp(-ageDays / 90));
        var substance = 0.5 + 0.5 * Math.Clamp(post.Substance, 0, 1);
        return Engagement(post) * recency * Math.Clamp(post.TrustWeight, 0, 1) * substance;
    }

    private static int WeightedScore(IReadOnlyCollection<SocialPost> posts, DateTime nowUtc)
    {
        double weighted = 0, total = 0;
        foreach (var p in posts)
        {
            var w = Weight(p, nowUtc);
            weighted += w * p.SentimentScore;
            total += w;
        }

        var mean = total > 0 ? weighted / total : 0;
        return (int)Math.Clamp(Math.Round(50 + 50 * mean), 0, 100);
    }
}
