using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

public sealed class SocialMeterService : ISocialMeterService
{
    private readonly SocialMeterDbContext _db;
    private readonly ITitleService _titles;
    private readonly IEnumerable<ISocialSource> _sources;
    private readonly ISentimentAnalyzer _sentiment;
    private readonly TrustPolicy _trust;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<SocialMeterService> _logger;

    public SocialMeterService(
        SocialMeterDbContext db,
        ITitleService titles,
        IEnumerable<ISocialSource> sources,
        ISentimentAnalyzer sentiment,
        TrustPolicy trust,
        IOptions<SocialMeterOptions> options,
        ILogger<SocialMeterService> logger)
    {
        _db = db;
        _titles = titles;
        _sources = sources;
        _sentiment = sentiment;
        _trust = trust;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SocialMeterResponseDto?> GetScoreAsync(MediaType mediaType, int tmdbId, bool forceRefresh, CancellationToken ct = default)
    {
        var title = await _titles.GetDetailsAsync(mediaType, tmdbId, ct);
        if (title is null)
            return null;

        var now = DateTime.UtcNow;
        var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
        var stale = snapshot is null || now - snapshot.LastRefreshedAt > TimeSpan.FromHours(_options.RefreshAfterHours);

        var sourceStatus = new List<SourceStatusDto>();
        var fromCache = true;

        if (forceRefresh || stale)
        {
            fromCache = false;
            foreach (var source in _sources)
                sourceStatus.Add(await IngestAsync(source, title, now, ct));
        }
        else
        {
            sourceStatus.AddRange(_sources.Select(s => new SourceStatusDto { Platform = s.Platform, Configured = s.IsConfigured }));
        }

        var posts = await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType)
            .ToListAsync(ct);

        if (!fromCache)
        {
            // Rules that look across the whole set (one voice per account, duplicates) run once everything is in.
            TrustPolicy.ApplyCrossPostRules(posts, now);
        }

        var meter = SocialScoreCalculator.Compute(posts, now, _options.MomentumWindowDays);

        if (!fromCache)
        {
            snapshot ??= _db.TitleSnapshots.Add(new TitleSnapshot { TmdbId = tmdbId, MediaType = mediaType }).Entity;
            snapshot.Score = meter.Score;
            snapshot.Label = meter.Label;
            snapshot.Confidence = meter.Confidence;
            snapshot.TotalPosts = meter.TotalPosts;
            snapshot.FilteredPosts = meter.FilteredPosts;
            snapshot.Positive = meter.Breakdown.Positive;
            snapshot.Neutral = meter.Breakdown.Neutral;
            snapshot.Negative = meter.Breakdown.Negative;
            snapshot.Momentum = meter.Momentum;

            // When every configured source failed, retry in ten minutes instead of caching the failure for the full window.
            var configured = sourceStatus.Where(s => s.Configured).ToList();
            var allFailed = configured.Count > 0 && configured.All(s => s.Error is not null);
            snapshot.LastRefreshedAt = allFailed ? now.AddHours(-_options.RefreshAfterHours).AddMinutes(10) : now;

            await _db.SaveChangesAsync(ct);
        }

        var top = posts
            .Where(p => !p.Excluded)
            .OrderByDescending(p => SocialScoreCalculator.Weight(p, now))
            .Take(_options.TopPosts)
            .Select(SocialPostDto.From)
            .ToList();

        return new SocialMeterResponseDto
        {
            TmdbId = tmdbId,
            MediaType = mediaType,
            TitleName = title.Name,
            Meter = meter,
            TopPosts = top,
            Sources = sourceStatus,
            LastRefreshedAt = snapshot?.LastRefreshedAt ?? now,
            FromCache = fromCache
        };
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

        return new PagedResult<SocialPostDto>(items.Select(SocialPostDto.From).ToList(), page, (int)Math.Ceiling(total / (double)pageSize), total);
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
                s => $"{Key(s.MediaType)}:{s.TmdbId}",
                s => new SnapshotDto
                {
                    Score = s.Score,
                    Label = s.Label,
                    TotalPosts = s.TotalPosts,
                    Confidence = s.Confidence,
                    Momentum = s.Momentum
                });
    }

    private async Task<SourceStatusDto> IngestAsync(ISocialSource source, Title title, DateTime now, CancellationToken ct)
    {
        if (!source.IsConfigured)
            return new SourceStatusDto { Platform = source.Platform, Configured = false };

        IReadOnlyList<SocialPostCandidate> candidates;
        try
        {
            candidates = await source.FetchAsync(title, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "{Platform} fetch failed for {Title}", source.Platform, title.Name);
            return new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = 0, Error = ex.Message };
        }

        if (candidates.Count == 0)
            return new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = 0 };

        var externalIds = candidates.Select(c => c.ExternalId).ToList();
        var existing = await _db.SocialPosts
            .Where(p => p.Platform == source.Platform && externalIds.Contains(p.ExternalId))
            .ToDictionaryAsync(p => p.ExternalId, ct);

        foreach (var c in candidates)
        {
            var analysis = _sentiment.Analyze(c.Content);
            var trust = _trust.Evaluate(c, now);

            if (existing.TryGetValue(c.ExternalId, out var post))
            {
                // Engagement and account standing keep moving after a post is first seen; keep them current.
                post.Upvotes = c.Upvotes;
                post.Comments = c.Comments;
                post.AuthorId ??= c.AuthorId;
                post.AuthorCreatedAt ??= c.AuthorCreatedAt;
                post.AuthorReputation = c.AuthorReputation ?? post.AuthorReputation;
                post.Sentiment = analysis.Label;
                post.SentimentScore = analysis.Score;
                post.TrustWeight = trust.Weight;
                post.Excluded = trust.Excluded;
                post.ExclusionReason = trust.ExclusionReason;
                post.FetchedAt = now;
                continue;
            }

            _db.SocialPosts.Add(new SocialPost
            {
                TmdbId = title.TmdbId,
                MediaType = title.MediaType,
                Platform = c.Platform,
                ExternalId = c.ExternalId,
                Author = Trim(c.Author, 100),
                AuthorId = c.AuthorId is null ? null : Trim(c.AuthorId, 64),
                AuthorCreatedAt = c.AuthorCreatedAt,
                AuthorReputation = c.AuthorReputation,
                Content = c.Content,
                ContentHash = TrustPolicy.ContentHash(c.Content),
                Sentiment = analysis.Label,
                SentimentScore = analysis.Score,
                TrustWeight = trust.Weight,
                Excluded = trust.Excluded,
                ExclusionReason = trust.ExclusionReason,
                Upvotes = c.Upvotes,
                Comments = c.Comments,
                CreatedAt = c.CreatedAt,
                FetchedAt = now,
                SourceUrl = c.SourceUrl is null ? null : Trim(c.SourceUrl, 500),
                Community = c.Community is null ? null : Trim(c.Community, 100)
            });
        }

        await _db.SaveChangesAsync(ct);
        return new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = candidates.Count };
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    public static string Key(MediaType mediaType) => mediaType == MediaType.Tv ? "tv" : "movie";
}
