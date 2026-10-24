using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Social;

/// <summary>Read-only queries over stored posts and cached readings. Never contacts a source.</summary>
public sealed class PostQueryService : IPostQueries
{
    public const int MaxPageSize = 12;
    public const int MaxPages = 10;

    private readonly SocialMeterDbContext _db;
    private readonly ScoreHistoryService _history;

    public PostQueryService(SocialMeterDbContext db, ScoreHistoryService history)
    {
        _db = db;
        _history = history;
    }

    public async Task<PagedResult<SocialPostDto>> GetPostsAsync(MediaType mediaType, int tmdbId, Sentiment? sentiment, string? platform, int page, int pageSize, CancellationToken ct = default)
    {
        // The page shows twelve at a time and loads more on request; capping both bounds what any client can harvest per title.
        page = Math.Clamp(page, 1, MaxPages);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _db.SocialPosts.Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded);
        if (sentiment is not null) query = query.Where(p => p.Sentiment == sentiment);
        if (!string.IsNullOrWhiteSpace(platform)) query = query.Where(p => p.Platform == platform);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.Upvotes)
            .ThenByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Each post's share of the whole title's weight, so the card can say "4 % of this score".
        var now = DateTime.UtcNow;
        var totalWeight = await TotalWeightAsync(mediaType, tmdbId, now, ct);

        return new PagedResult<SocialPostDto>(
            items.Select(p => SocialPostDto.From(p, SocialScoreCalculator.Share(p, totalWeight, now))).ToList(),
            page, (int)Math.Ceiling(total / (double)pageSize), total);
    }

    public async Task<SocialPostDto?> GetPostAsync(MediaType mediaType, int tmdbId, int postId, CancellationToken ct = default)
    {
        // Counted posts of this title only: a hidden or filtered post, or another title's, is "not found".
        var post = await _db.SocialPosts.SingleOrDefaultAsync(p => p.Id == postId && p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded, ct);
        if (post is null) return null;

        var now = DateTime.UtcNow;
        return SocialPostDto.From(post, SocialScoreCalculator.Share(post, await TotalWeightAsync(mediaType, tmdbId, now, ct), now));
    }

    /// <summary>The title's total counted weight (numbers only, no post text), for a post's share of the score.</summary>
    private async Task<double> TotalWeightAsync(MediaType mediaType, int tmdbId, DateTime now, CancellationToken ct) =>
        (await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded)
            .Select(p => new SocialPost { Upvotes = p.Upvotes, Comments = p.Comments, CreatedAt = p.CreatedAt, TrustWeight = p.TrustWeight, Substance = p.Substance })
            .ToListAsync(ct))
        .Sum(p => SocialScoreCalculator.Weight(p, now));

    public async Task<IReadOnlyList<SocialMeter.Application.Home.TalkedAbout>> GetMostTalkedAboutAsync(DateTime since, int minPosts, int take, CancellationToken ct = default)
    {
        // Counted posts only: off-topic, untrusted and duplicate posts are excluded, so this is real conversation.
        var counts = await _db.SocialPosts
            .Where(p => !p.Excluded && p.CreatedAt >= since)
            .GroupBy(p => new { p.MediaType, p.TmdbId })
            .Select(g => new { g.Key.MediaType, g.Key.TmdbId, Posts = g.Count() })
            .Where(x => x.Posts >= minPosts)
            .OrderByDescending(x => x.Posts)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);
        if (counts.Count == 0)
            return Array.Empty<SocialMeter.Application.Home.TalkedAbout>();

        var ids = counts.Select(c => c.TmdbId).Distinct().ToList();
        var confidence = (await _db.TitleSnapshots.Where(s => ids.Contains(s.TmdbId)).ToListAsync(ct))
            .ToDictionary(s => (s.MediaType, s.TmdbId), s => s.Confidence);

        return counts
            .Select(c => new SocialMeter.Application.Home.TalkedAbout(c.MediaType, c.TmdbId, c.Posts,
                confidence.TryGetValue((c.MediaType, c.TmdbId), out var conf) ? conf : 0))
            .ToList();
    }

    public async Task<IReadOnlyList<SocialMeter.Contracts.Seo.SitemapTitleDto>> GetScoredTitlesAsync(int take, CancellationToken ct = default) =>
        await _db.TitleSnapshots.AsNoTracking()
            .Where(s => s.TotalPosts > 0)
            .OrderByDescending(s => s.LastRefreshedAt)
            .Take(take)
            .Select(s => new SocialMeter.Contracts.Seo.SitemapTitleDto(s.MediaType, s.TmdbId, s.LastRefreshedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<string, SnapshotDto>> GetSnapshotsAsync(IEnumerable<(MediaType MediaType, int TmdbId)> keys, CancellationToken ct = default)
    {
        var list = keys.Distinct().ToList();
        if (list.Count == 0)
            return new Dictionary<string, SnapshotDto>();

        var ids = list.Select(k => k.TmdbId).Distinct().ToList();
        var snapshots = await _db.TitleSnapshots.Where(s => ids.Contains(s.TmdbId)).ToListAsync(ct);
        var wanted = list.ToHashSet();

        return snapshots
            .Where(s => wanted.Contains((s.MediaType, s.TmdbId)))
            .ToDictionary(
                s => $"{s.MediaType.Segment()}:{s.TmdbId}",
                s => new SnapshotDto
                {
                    Score = s.Score,
                    Label = s.Label,
                    TotalPosts = s.TotalPosts,
                    Confidence = s.Confidence,
                    Margin = s.Margin,
                    CriticsScore = s.CriticsScore
                });
    }

    public async Task<IReadOnlyList<SocialMeter.Application.Home.CrowdCritics>> GetCrowdCriticsAsync(CancellationToken ct = default)
        => await _db.TitleSnapshots
            .Where(s => s.MediaType != MediaType.Game && s.CriticsScore != null && s.TotalPosts > 0)
            .Select(s => new SocialMeter.Application.Home.CrowdCritics(s.MediaType, s.TmdbId, s.Score, s.CriticsScore!.Value, s.TotalPosts, s.Confidence))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(MediaType MediaType, int TmdbId, IReadOnlyList<(DateOnly Day, int Score)> Days)>> GetScoreSeriesAsync(
        DateOnly since, int minPosts, CancellationToken ct = default)
    {
        var scored = await _db.TitleSnapshots.Where(s => s.TotalPosts >= minPosts).Select(s => new { s.MediaType, s.TmdbId }).ToListAsync(ct);
        var wanted = scored.Select(s => (s.MediaType, s.TmdbId)).ToHashSet();
        var ids = scored.Select(s => s.TmdbId).Distinct().ToList();
        var rows = await _db.ScoreHistory.Where(h => h.Day >= since && ids.Contains(h.TmdbId))
            .Select(h => new { h.MediaType, h.TmdbId, h.Day, h.Score }).ToListAsync(ct);

        return rows.Where(r => wanted.Contains((r.MediaType, r.TmdbId)))
            .GroupBy(r => (r.MediaType, r.TmdbId))
            .Select(g => (g.Key.MediaType, g.Key.TmdbId, (IReadOnlyList<(DateOnly, int)>)g.Select(r => (r.Day, r.Score)).ToList()))
            .ToList();
    }

    public Task<ScoreHistoryDto> GetHistoryAsync(MediaType mediaType, int tmdbId, int days, CancellationToken ct = default)
        => _history.GetAsync(mediaType, tmdbId, days, DateTime.UtcNow, ct);
}
