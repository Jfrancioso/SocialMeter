using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Social;

/// <summary>Read-only queries over stored posts and cached readings. Never contacts a source.</summary>
public sealed class PostQueryService : IPostQueries
{
    private readonly SocialMeterDbContext _db;
    private readonly ScoreHistoryService _history;

    public PostQueryService(SocialMeterDbContext db, ScoreHistoryService history)
    {
        _db = db;
        _history = history;
    }

    public async Task<PagedResult<SocialPostDto>> GetPostsAsync(MediaType mediaType, int tmdbId, Sentiment? sentiment, string? platform, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

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
        var totalWeight = (await _db.SocialPosts
                .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && !p.Excluded)
                .Select(p => new SocialPost { Upvotes = p.Upvotes, Comments = p.Comments, CreatedAt = p.CreatedAt, TrustWeight = p.TrustWeight, Substance = p.Substance })
                .ToListAsync(ct))
            .Sum(p => SocialScoreCalculator.Weight(p, now));

        return new PagedResult<SocialPostDto>(
            items.Select(p => SocialPostDto.From(p, SocialScoreCalculator.Share(p, totalWeight, now))).ToList(),
            page, (int)Math.Ceiling(total / (double)pageSize), total);
    }

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
                    Margin = s.Margin
                });
    }

    public Task<ScoreHistoryDto> GetHistoryAsync(MediaType mediaType, int tmdbId, int days, CancellationToken ct = default)
        => _history.GetAsync(mediaType, tmdbId, days, DateTime.UtcNow, ct);
}
