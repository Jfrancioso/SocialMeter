using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Home;
using SocialMeter.Application.Tracking;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// The shape of a title's crowd, from the shares of its counted weight, and which titles show it. Pure, so the tests pin
/// the thresholds down.
///
/// Why it exists: almost no post is neutral (0-20% of the weight on every title checked 2026-10-23), so a middling score
/// usually means a fight, not a shrug. Other Mommy read "49 Lukewarm" while 45% of the weight loved it and 43% hated it.
/// </summary>
public static class CrowdShapeRules
{
    /// <summary>Below this many counted posts the shape is "early": too few to call.</summary>
    public const int EarlyBelow = 10;

    /// <summary>Both sides at least this share (percent): the crowd is split.</summary>
    public const double SplitShare = 30;

    public const double LovedShare = 70, PannedShare = 60, MixedShare = 40;

    public const string Split = "split", Loved = "loved", Panned = "panned", Mixed = "mixed",
        LeaningPositive = "leaning-positive", LeaningNegative = "leaning-negative", Early = "early";

    /// <summary>Shares are percentages of the counted weight; checked in this order.</summary>
    public static string Shape(double positive, double negative, double neutral, int posts)
    {
        if (posts < EarlyBelow) return Early;
        if (positive >= SplitShare && negative >= SplitShare) return Split;
        if (positive >= LovedShare) return Loved;
        if (negative >= PannedShare) return Panned;
        if (neutral >= MixedShare) return Mixed;
        return positive >= negative ? LeaningPositive : LeaningNegative;
    }

    /// <summary>Who gets "The crowd": a listed title or a live event always; a most-talked-about title once it has enough posts.</summary>
    public static bool Shows(bool listed, bool liveEvent, bool talkedAbout, int posts, int minPosts) =>
        listed || liveEvent || (talkedAbout && posts >= minPosts);

    /// <summary>
    /// How many dots each side gets when not every post can be drawn: in proportion to its share of the weight, so the
    /// dots tell the same story as the shares. (Taking the heaviest posts overall did not: on WARDOGS the hated reviews
    /// weigh more one by one, so the heaviest 100 were 98 hated and 2 loved, though loved held 22% of the weight.)
    /// Every side with posts gets one first, then each next dot goes to the side with the most share per dot (the
    /// D'Hondt method), never more than a side has. The front end's pickDots follows the same rule.
    /// </summary>
    public static IReadOnlyDictionary<Sentiment, int> DotsPerSide(IReadOnlyDictionary<Sentiment, (int Posts, double Share)> sides, int maxDots)
    {
        var given = sides.Keys.ToDictionary(s => s, _ => 0);
        var open = sides.Where(s => s.Value.Posts > 0).Select(s => s.Key).ToList();
        var left = Math.Max(0, maxDots);

        foreach (var s in open.OrderByDescending(s => sides[s].Share))
        {
            if (left == 0) break;
            given[s] = 1;
            left--;
        }
        while (left > 0)
        {
            var next = open.Where(s => given[s] < sides[s].Posts)
                .OrderByDescending(s => sides[s].Share / (given[s] + 1))
                .Cast<Sentiment?>()
                .FirstOrDefault();
            if (next is null) break;
            given[next.Value]++;
            left--;
        }
        return given;
    }
}

/// <summary>
/// "The crowd" for a title page: every counted post as a dot, no text. Reads stored posts only; never contacts the sources.
///
/// Guardrails, cheapest first: the switch and the title checks need no database; then one snapshot row says whether the
/// reading changed since the crowd was last built (a new read moves LastRefreshedAt, a hidden post moves the counts),
/// and only a changed reading reads the posts again. So a busy title costs one small query per view, not every post.
/// </summary>
public sealed class CrowdShapeService : ICrowdShape
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
    private static readonly CrowdShapeDto Off = new() { Enabled = false };

    private readonly SocialMeterDbContext _db;
    private readonly CrowdShapeOptions _options;
    private readonly WatchWindows _watch;
    private readonly MostTalkedAbout _talkedAbout;
    private readonly IMemoryCache _cache;

    public CrowdShapeService(SocialMeterDbContext db, IOptions<CrowdShapeOptions> options, WatchWindows watch, MostTalkedAbout talkedAbout, IMemoryCache cache)
    {
        _db = db;
        _options = options.Value;
        _watch = watch;
        _talkedAbout = talkedAbout;
        _cache = cache;
    }

    public async Task<CrowdShapeDto> GetAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return Off;

        var key = $"{mediaType.ToString().ToLowerInvariant()}_{tmdbId}";
        var listed = _options.IsListed(key);
        var live = _watch.OpenFor(mediaType, tmdbId) is not null;
        var talkedAbout = !listed && !live && _options.TopTalkedAbout > 0
            && (await _talkedAbout.GetAsync(ct)).Take(_options.TopTalkedAbout).Any(t => t.MediaType == mediaType && t.TmdbId == tmdbId);
        if (!listed && !live && !talkedAbout) return Off;

        var stamp = await _db.TitleSnapshots
            .Where(s => s.TmdbId == tmdbId && s.MediaType == mediaType)
            .Select(s => new { s.LastRefreshedAt, s.TotalPosts, s.FilteredPosts })
            .SingleOrDefaultAsync(ct);
        if (stamp is null) return Off;

        var crowd = await _cache.GetOrCreateAsync($"crowd:{key}:{stamp.LastRefreshedAt.Ticks}:{stamp.TotalPosts}:{stamp.FilteredPosts}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return BuildAsync(mediaType, tmdbId, ct);
        });

        return crowd is not null && CrowdShapeRules.Shows(listed, live, talkedAbout, crowd.Posts, _options.MinPosts) ? crowd : Off;
    }

    private async Task<CrowdShapeDto> BuildAsync(MediaType mediaType, int tmdbId, CancellationToken ct)
    {
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

        // When not every post can be drawn, each side's heaviest, in proportion to its share (see DotsPerSide).
        var perSide = CrowdShapeRules.DotsPerSide(new Dictionary<Sentiment, (int, double)>
        {
            [Sentiment.Positive] = (posts.Count(p => p.Sentiment == Sentiment.Positive), positive),
            [Sentiment.Negative] = (posts.Count(p => p.Sentiment == Sentiment.Negative), negative),
            [Sentiment.Neutral] = (posts.Count(p => p.Sentiment == Sentiment.Neutral), neutral)
        }, _options.MaxDots);
        var drawn = posts.GroupBy(p => p.Sentiment)
            .SelectMany(g => g.OrderByDescending(p => weights[p.Id]).Take(perSide[g.Key]))
            .OrderByDescending(p => weights[p.Id]);

        return new CrowdShapeDto
        {
            Enabled = true,
            Shape = CrowdShapeRules.Shape(positive, negative, neutral, posts.Count),
            Posts = posts.Count,
            Positive = positive,
            Negative = negative,
            Neutral = neutral,
            // Heaviest first (the entrance lands them in this order); a page with room for fewer picks again by side.
            Dots = drawn
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
