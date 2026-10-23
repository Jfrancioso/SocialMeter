using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Common;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>The "why is the score what it is" use case: weight shares, substance mix, top drivers, filter reasons and plain-English notes. Reads only.</summary>
public sealed class ScoreExplainer : IScoreExplainer
{
    private readonly SocialMeterDbContext _db;
    private readonly SocialMeterOptions _options;

    public ScoreExplainer(SocialMeterDbContext db, IOptions<SocialMeterOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<ScoreExplanationDto?> ExplainAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default)
    {
        var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
        if (snapshot is null) return null;

        var now = DateTime.UtcNow;
        var all = await _db.SocialPosts.Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType).ToListAsync(ct);
        var meter = SocialScoreCalculator.Compute(all, now, _options.Scoring);
        var counted = all.Where(p => !p.Excluded).ToList();
        var weights = counted.ToDictionary(p => p.Id, p => SocialScoreCalculator.Weight(p, now));
        var totalWeight = weights.Values.Sum();
        double Pct(IEnumerable<SocialPost> ps) => totalWeight > 0 ? Math.Round(100 * ps.Sum(p => weights[p.Id]) / totalWeight, 1) : 0;

        var mix = new[] { "bare", "some", "detailed" }
            .Select(level => new { level, ps = counted.Where(p => SocialPostDto.SubstanceLevelFor(p.Substance) == level).ToList() })
            .Select(g => new MixDto { Level = g.level, Posts = g.ps.Count, WeightShare = Pct(g.ps) })
            .ToList();

        var drivers = counted
            .OrderByDescending(p => weights[p.Id])
            .Take(6)
            .Select(p => new DriverDto
            {
                Id = p.Id,
                Platform = p.Platform,
                Author = p.Author,
                Excerpt = p.Content.ReplaceLineEndings(" ").Clip(140),
                Sentiment = p.Sentiment,
                SentimentScore = Math.Round(p.SentimentScore, 2),
                SubstanceLevel = SocialPostDto.SubstanceLevelFor(p.Substance),
                TrustLevel = SocialPostDto.TrustLevelFor(p.TrustWeight),
                WeightShare = Pct(new[] { p }),
                CreatedAt = p.CreatedAt,
                SourceUrl = p.SourceUrl
            })
            .ToList();

        var platforms = meter.Platforms
            .Select(ps => new PlatformShareDto { Platform = ps.Platform, Posts = ps.Posts, Score = ps.Score, WeightShare = Pct(counted.Where(p => p.Platform == ps.Platform)) })
            .ToList();

        var filtered = all.Where(p => p.Excluded)
            .GroupBy(p => p.Hidden ? Moderation.PostModeration.PublicReason : p.ExclusionReason ?? "set aside")
            .Select(g => new ReasonCountDto { Reason = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ToList();

        return new ScoreExplanationDto
        {
            Score = meter.Score,
            Label = meter.Label,
            Stats = meter.Stats,
            Confidence = meter.Confidence,
            ConfidenceLabel = meter.ConfidenceLabel,
            TotalPosts = meter.TotalPosts,
            FilteredPosts = meter.FilteredPosts,
            SentimentWeight = new ShareDto
            {
                Positive = Pct(counted.Where(p => p.Sentiment == Sentiment.Positive)),
                Neutral = Pct(counted.Where(p => p.Sentiment == Sentiment.Neutral)),
                Negative = Pct(counted.Where(p => p.Sentiment == Sentiment.Negative))
            },
            SubstanceMix = mix,
            Platforms = platforms,
            Drivers = drivers,
            Filtered = filtered,
            Notes = Notes(meter, counted.Count, drivers, mix, platforms)
        };
    }

    private List<string> Notes(SocialMeterScore meter, int counted, List<DriverDto> drivers, List<MixDto> mix, List<PlatformShareDto> platforms)
    {
        var notes = new List<string>();
        if (counted == 0) return notes;

        var k = _options.PriorStrength;
        if (meter.Stats.EffectivePosts < k)
            notes.Add($"Fewer than {k:0} effective posts, so the neutral prior still carries {1 - meter.Confidence:P0} of the score.");
        if (meter.Stats.PriorPull != 0)
            notes.Add($"The raw blend of tone and approval is {meter.Stats.RawScore}; the pull toward neutral moved it {Math.Abs(meter.Stats.PriorPull)} point{(Math.Abs(meter.Stats.PriorPull) == 1 ? "" : "s")} {(meter.Stats.PriorPull > 0 ? "up" : "down")}.");
        if (drivers.Count > 0 && drivers[0].WeightShare >= 20)
            notes.Add($"One post carries {drivers[0].WeightShare:0}% of the weight.");
        var detailed = mix.Single(m => m.Level == "detailed");
        notes.Add($"Posts with specifics are {detailed.Posts} of {counted} but carry {detailed.WeightShare:0}% of the weight.");
        if (platforms.Count > 1 && platforms.Max(p => p.Score) - platforms.Min(p => p.Score) >= 15)
            notes.Add($"Platforms disagree: {string.Join(", ", platforms.Select(p => $"{PlatformName(p.Platform)} {p.Score}"))}.");
        return notes;
    }

    private static string PlatformName(string platform) => platform switch { "x" => "X", "reddit" => "Reddit", "steam" => "Steam", "bluesky" => "Bluesky", _ => platform };

}
