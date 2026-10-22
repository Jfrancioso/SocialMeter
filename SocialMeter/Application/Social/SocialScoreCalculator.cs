using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Social;

/// <summary>Tunable parameters of the aggregation. Not secrets (the "how it works" page describes them) but kept in options so they can be tuned against real titles.</summary>
public sealed record ScoringParameters(double PriorStrength, double MinSigma)
{
    public static readonly ScoringParameters Default = new(6, 0.2);
}

/// <summary>
/// Turns a set of analyzed posts into one SocialMeter reading. Pure and deterministic.
/// Each post's sentiment is weighted by community engagement (log-scaled so one viral post can't swamp the rest), by
/// recency, by the trust the policy assigned to its author and by how much substance it carries. The weighted mean is
/// then pulled toward neutral by a Bayesian average until enough effective posts accumulate, and reported with a 95 %
/// margin so a reading built on three posts never looks as solid as one built on three hundred. Excluded posts never take part.
/// </summary>
public static class SocialScoreCalculator
{
    public static SocialMeterScore Compute(IReadOnlyCollection<SocialPost> allPosts, DateTime nowUtc, ScoringParameters? parameters = null)
    {
        var prm = parameters ?? ScoringParameters.Default;
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

        var reading = Read(posts, nowUtc, prm);

        var platforms = posts
            .GroupBy(p => p.Platform)
            .Select(g => new PlatformSummary { Platform = g.Key, Posts = g.Count(), Score = Read(g.ToList(), nowUtc, prm).Score })
            .OrderByDescending(p => p.Posts)
            .ToList();

        return new SocialMeterScore
        {
            Score = reading.Score,
            Label = SocialMeterScore.LabelFor(reading.Score),
            Confidence = reading.Confidence,
            ConfidenceLabel = SocialMeterScore.ConfidenceLabelFor(reading.Confidence),
            TotalPosts = posts.Count,
            FilteredPosts = filtered,
            DetailedPosts = posts.Count(p => p.Substance >= 0.6),
            Breakdown = new SentimentBreakdown
            {
                Positive = posts.Count(p => p.Sentiment == Sentiment.Positive),
                Neutral = posts.Count(p => p.Sentiment == Sentiment.Neutral),
                Negative = posts.Count(p => p.Sentiment == Sentiment.Negative)
            },
            Stats = new ScoreStats
            {
                RawScore = reading.RawScore,
                EffectivePosts = Math.Round(reading.EffectivePosts, 1),
                Margin = reading.Margin,
                Lower = Math.Max(0, reading.Score - reading.Margin),
                Upper = Math.Min(100, reading.Score + reading.Margin),
                PriorPull = reading.Score - reading.RawScore
            },
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
        return Engagement(post) * recency * Credit(post);
    }

    /// <summary>This post's share of a title's total weight, 0–100; null when there is no weight to share.</summary>
    public static double? Share(SocialPost post, double totalWeight, DateTime nowUtc)
        => totalWeight > 0 ? 100 * Weight(post, nowUtc) / totalWeight : null;

    /// <summary>The fraction of a full post this one is worth on trust and substance alone (0–1).</summary>
    public static double Credit(SocialPost post)
        => Math.Clamp(post.TrustWeight, 0, 1) * (0.5 + 0.5 * Math.Clamp(post.Substance, 0, 1));

    public readonly record struct Reading(int Score, int RawScore, double EffectivePosts, int Margin, double Confidence);

    /// <summary>The headline numbers for a set of counted posts. Exposed so the explain endpoint and the history backfill use the same maths.</summary>
    public static Reading Read(IReadOnlyList<SocialPost> posts, DateTime nowUtc, ScoringParameters prm)
    {
        if (posts.Count == 0) return new Reading(50, 50, 0, 50, 0);

        var items = posts.Select(p => (p.SentimentScore, Weight(p, nowUtc))).ToList();
        var summary = ScoreStatistics.Summarize(items);

        // Kish answers "how many equal posts is this weight profile worth"; the average credit answers "how much of a post is each one".
        var effective = summary.KishN * posts.Average(Credit);

        var shrunkMean = ScoreStatistics.Shrink(summary.Mean, effective, prior: 0, k: prm.PriorStrength);
        var shrinkFactor = prm.PriorStrength <= 0 ? 1 : effective / (effective + prm.PriorStrength);

        // The interval is on the data mean, scaled by the same factor the mean was: a single post barely moves the score, and its interval barely widens it.
        var margin = ScoreStatistics.Margin95(summary, prm.MinSigma) * shrinkFactor;

        return new Reading(
            ToPoints(shrunkMean),
            ToPoints(summary.Mean),
            effective,
            (int)Math.Clamp(Math.Round(50 * margin), 1, 50),
            Math.Round(shrinkFactor, 2));
    }

    private static int ToPoints(double mean) => (int)Math.Clamp(Math.Round(50 + 50 * mean), 0, 100);
}
