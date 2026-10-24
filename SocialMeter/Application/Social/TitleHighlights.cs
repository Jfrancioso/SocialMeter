using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Social;

/// <summary>One side of a reading: its counted posts and its share of the title's counted weight (0–100).</summary>
public sealed record SideTally(Sentiment Sentiment, int Posts, double WeightShare);

/// <summary>
/// The rules behind the title page's "In their words": which side gets a featured post, and which post. Pure, so the
/// tests can pin them down without a database.
///
/// The featured post on a side is the one that ranks highest on its weight in the score (engagement × recency × trust ×
/// substance) times how strongly it leans that way, among posts that read well on their own. The lean matters: on real
/// posts, weight alone featured a political aside over "season 2 is somehow even better than the first" (2026-10-23).
/// The text is shown exactly as posted (X's display rules); a post that fails a rule is skipped, never edited.
/// </summary>
public static class HighlightRules
{
    /// <summary>A side needs this many counted posts to be featured...</summary>
    public const int MinPosts = 3;

    /// <summary>...and this share of the counted weight (percent), so two complaints among sixty raves don't get equal billing.</summary>
    public const double MinShare = 10;

    /// <summary>Shorter is usually a bare reaction; longer is a wall of text in the spot that should read at a glance.</summary>
    public const int MinLength = 50, MaxLength = 600;

    /// <summary>Letters left once links, @mentions and #hashtags are gone; below this the post is mostly links or tags.</summary>
    public const int MinLetters = 40;

    /// <summary>Substance below this is a bare reaction ("10/10", "peak"); see SocialPostDto.SubstanceLevelFor.</summary>
    public const double MinSubstance = 0.3;

    // A featured post sits near the top of the page, so crude or abusive wording rules a post out (the feed below still
    // shows it; this only decides what is featured). Roots are matched anywhere in a word, the rest as whole words.
    private static readonly string[] BlockedRoots = { "fuck", "shit", "bitch", "cunt", "nigg", "faggot", "retard", "whore", "slut", "pedophil", "paedophil", "rapist", "porn", "molest" };

    private static readonly HashSet<string> BlockedWords = new(StringComparer.Ordinal)
    {
        "rape", "raped", "rapes", "raping", "pedo", "pedos", "fag", "fags", "dick", "dicks", "cock", "cocks", "pussy", "ass",
        "asshole", "assholes", "tits", "cum", "kys", "twat", "wank", "wanker", "dyke", "tranny", "spic", "chink", "kike", "gook",
        "fk", "fking", "fkin", "fkn", "stfu"
    };

    /// <summary>Above this share of capital letters the post is shouting, which reads badly as the featured take.</summary>
    public const double MaxCapitals = 0.6;

    private static readonly Regex Noise = new(@"https?://\S+|\bwww\.\S+|[@#]\w+", RegexOptions.Compiled);
    private static readonly Regex Words = new(@"[a-z]+", RegexOptions.Compiled);

    /// <summary>Counted posts and weight per side. Neutral posts count toward the total weight but are never featured.</summary>
    public static IReadOnlyList<SideTally> Tally(IReadOnlyCollection<(Sentiment Sentiment, double Weight)> posts)
    {
        var total = posts.Sum(p => p.Weight);
        return new[] { Sentiment.Positive, Sentiment.Negative }
            .Select(s =>
            {
                var side = posts.Where(p => p.Sentiment == s).ToList();
                return new SideTally(s, side.Count, total > 0 ? Math.Round(100 * side.Sum(p => p.Weight) / total, 1) : 0);
            })
            .ToList();
    }

    public static bool Featured(SideTally side) => side.Posts >= MinPosts && side.WeightShare >= MinShare;

    /// <summary>
    /// The checks that need only the numbers. Only established authors (full trust, the card's "established" tick) are
    /// featured: a post the trust policy discounts still counts, but the page shouldn't put it forward as the take.
    /// </summary>
    public static bool Eligible(SocialPost post) =>
        !post.Excluded && !post.Sensitive && post.Substance >= MinSubstance && post.TrustWeight >= 1;

    /// <summary>True when the post has images or video attached (stored as a JSON array; null or empty when none).</summary>
    public static bool HasMedia(string? mediaJson) => !string.IsNullOrEmpty(mediaJson) && mediaJson != "[]";

    /// <summary>Whether a post can be featured as written: eligible, words only, a readable length, no spoiler warning, no crude wording.</summary>
    public static bool IsQuotable(SocialPost post)
    {
        if (!Eligible(post)) return false;

        // "In their words": a clip or meme is a different kind of post (on real data, a "fans walked out over this
        // scene" video), and its media made one card several times taller than the other.
        if (HasMedia(post.MediaJson)) return false;
        var text = post.Content.Trim();
        if (text.Length is < MinLength or > MaxLength) return false;

        // A post that warns about spoilers ("MILD SPOILER", "spoilers ahead") is one; the shield only covers it until a tap.
        var lower = text.ToLowerInvariant();
        if (lower.Contains("spoil")) return false;

        var plain = Noise.Replace(lower, " ");
        var letters = plain.Count(char.IsLetter);
        if (letters < MinLetters) return false;

        // Capitals counted on the original text, links and tags removed (a title in capitals is fine; a whole post isn't).
        if (Noise.Replace(text, " ").Count(char.IsUpper) > MaxCapitals * letters) return false;

        foreach (Match m in Words.Matches(plain))
        {
            var w = m.Value;
            if (BlockedWords.Contains(w) || BlockedRoots.Any(w.Contains)) return false;
        }
        return true;
    }

    /// <summary>Weight in the score times strength of the lean (tone, 0–1): a clear take beats a vaguer post with a few more likes.</summary>
    public static double Rank(SocialPost post, double weight) => weight * Math.Abs(Math.Clamp(post.SentimentScore, -1, 1));

    /// <summary>The highest-ranked quotable post; the newer one on a tie. Null when none qualifies.</summary>
    public static SocialPost? Pick(IEnumerable<(SocialPost Post, double Weight)> side) =>
        side.Where(c => IsQuotable(c.Post))
            .OrderByDescending(c => Rank(c.Post, c.Weight))
            .ThenByDescending(c => c.Post.CreatedAt)
            .Select(c => c.Post)
            .FirstOrDefault();
}

/// <summary>"In their words" for a title page: the featured post on each side that qualifies. Reads only, never contacts the sources.</summary>
public sealed class TitleHighlights : ITitleHighlights
{
    // Full text is loaded for this many of each side's top-ranked eligible posts; the rest are never read.
    private const int Candidates = 20;

    private readonly SocialMeterDbContext _db;

    public TitleHighlights(SocialMeterDbContext db) => _db = db;

    public async Task<TitleHighlightsDto> GetAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Pass 1: the numbers only (no post text), for every counted post. Same projection trick as PostQueryService.
        var rows = await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded)
            .Select(p => new
            {
                Post = new SocialPost
                {
                    Id = p.Id, Sentiment = p.Sentiment, SentimentScore = p.SentimentScore, Upvotes = p.Upvotes, Comments = p.Comments,
                    CreatedAt = p.CreatedAt, TrustWeight = p.TrustWeight, Substance = p.Substance, Sensitive = p.Sensitive
                },
                Length = p.Content.Length,
                HasMedia = p.MediaJson != null && p.MediaJson != "" && p.MediaJson != "[]"
            })
            .ToListAsync(ct);
        if (rows.Count == 0) return new TitleHighlightsDto();

        var weights = rows.ToDictionary(r => r.Post.Id, r => SocialScoreCalculator.Weight(r.Post, now));
        var totalWeight = weights.Values.Sum();
        var tallies = HighlightRules.Tally(rows.Select(r => (r.Post.Sentiment, weights[r.Post.Id])).ToList());

        // Pass 2: full text for the top-ranked few eligible posts on each featured side. The length here is before
        // trimming, which is close enough for a pre-filter; IsQuotable makes the final call on the text.
        var candidateIds = tallies.Where(HighlightRules.Featured)
            .SelectMany(t => rows
                .Where(r => r.Post.Sentiment == t.Sentiment && HighlightRules.Eligible(r.Post) && !r.HasMedia
                            && r.Length is >= HighlightRules.MinLength and <= HighlightRules.MaxLength)
                .OrderByDescending(r => HighlightRules.Rank(r.Post, weights[r.Post.Id]))
                .Take(Candidates)
                .Select(r => r.Post.Id))
            .ToList();
        var full = candidateIds.Count == 0
            ? new List<SocialPost>()
            : await _db.SocialPosts.Where(p => candidateIds.Contains(p.Id)).ToListAsync(ct);

        HighlightDto Side(Sentiment s)
        {
            var tally = tallies.Single(t => t.Sentiment == s);
            var featured = HighlightRules.Featured(tally);
            var pick = featured ? HighlightRules.Pick(full.Where(p => p.Sentiment == s).Select(p => (p, weights[p.Id]))) : null;
            return new HighlightDto
            {
                Posts = tally.Posts,
                WeightShare = tally.WeightShare,
                Featured = featured,
                Post = pick is null ? null : SocialPostDto.From(pick, SocialScoreCalculator.Share(pick, totalWeight, now))
            };
        }

        return new TitleHighlightsDto { Positive = Side(Sentiment.Positive), Negative = Side(Sentiment.Negative) };
    }
}
