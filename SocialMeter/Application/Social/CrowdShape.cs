using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// The shape of a title's crowd, from the shares of its counted weight. Pure, so the tests pin the thresholds down.
///
/// Why it exists: almost no post is neutral (0-20% of the weight on every title checked 2026-10-23), so a middling score
/// usually means a fight, not a shrug. Other Mommy read "49 Lukewarm" while 45% of the weight loved it and 43% hated it.
/// </summary>
public static class CrowdShapeRules
{
    /// <summary>Below this many counted posts the shape is "early": too few to call.</summary>
    public const int MinPosts = 10;

    /// <summary>Both sides at least this share (percent): the crowd is split.</summary>
    public const double SplitShare = 30;

    public const double LovedShare = 70, PannedShare = 60, MixedShare = 40;

    public const string Split = "split", Loved = "loved", Panned = "panned", Mixed = "mixed",
        LeaningPositive = "leaning-positive", LeaningNegative = "leaning-negative", Early = "early";

    /// <summary>Shares are percentages of the counted weight; checked in this order.</summary>
    public static string Shape(double positive, double negative, double neutral, int posts)
    {
        if (posts < MinPosts) return Early;
        if (positive >= SplitShare && negative >= SplitShare) return Split;
        if (positive >= LovedShare) return Loved;
        if (negative >= PannedShare) return Panned;
        if (neutral >= MixedShare) return Mixed;
        return positive >= negative ? LeaningPositive : LeaningNegative;
    }
}

/// <summary>"The crowd" for pilot titles (CrowdShape:Titles). Reads stored posts only; never contacts the sources.</summary>
public sealed class CrowdShapeService : ICrowdShape
{
    private readonly SocialMeterDbContext _db;
    private readonly CrowdShapeOptions _options;

    public CrowdShapeService(SocialMeterDbContext db, IOptions<CrowdShapeOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<CrowdShapeDto> GetAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default)
    {
        // Outside the pilot: answered without touching the database.
        if (!_options.IsOn($"{mediaType.ToString().ToLowerInvariant()}_{tmdbId}"))
            return new CrowdShapeDto { Enabled = false };

        var now = DateTime.UtcNow;

        // Numbers only, no post text: the same projection trick as PostQueryService.
        var posts = await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded)
            .Select(p => new SocialPost
            {
                Id = p.Id, Platform = p.Platform, Sentiment = p.Sentiment, SentimentScore = p.SentimentScore, Upvotes = p.Upvotes,
                Comments = p.Comments, CreatedAt = p.CreatedAt, TrustWeight = p.TrustWeight, Substance = p.Substance
            })
            .ToListAsync(ct);

        var weights = posts.ToDictionary(p => p.Id, p => SocialScoreCalculator.Weight(p, now));
        var total = weights.Values.Sum();
        double Pct(Sentiment s) => total > 0 ? Math.Round(100 * posts.Where(p => p.Sentiment == s).Sum(p => weights[p.Id]) / total, 1) : 0;

        var positive = Pct(Sentiment.Positive);
        var negative = Pct(Sentiment.Negative);
        var neutral = Pct(Sentiment.Neutral);

        return new CrowdShapeDto
        {
            Enabled = true,
            Shape = CrowdShapeRules.Shape(positive, negative, neutral, posts.Count),
            Posts = posts.Count,
            Positive = positive,
            Negative = negative,
            Neutral = neutral,
            Dots = posts
                .OrderByDescending(p => weights[p.Id])
                .Take(Math.Max(0, _options.MaxDots))
                .Select(p => new CrowdDotDto
                {
                    Id = p.Id,
                    Platform = p.Platform,
                    Sentiment = p.Sentiment.ToString().ToLowerInvariant(),
                    Value = Math.Round(SocialScoreCalculator.Value(p), 3),
                    Share = total > 0 ? Math.Round(100 * weights[p.Id] / total, 2) : 0
                })
                .ToList()
        };
    }
}
