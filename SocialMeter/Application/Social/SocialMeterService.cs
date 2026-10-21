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
    private readonly IPostAssessor _assessor;
    private readonly ITitleTracker _tracker;
    private readonly TrustPolicy _trust;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<SocialMeterService> _logger;

    public SocialMeterService(
        SocialMeterDbContext db,
        ITitleService titles,
        IEnumerable<ISocialSource> sources,
        IPostAssessor assessor,
        ITitleTracker tracker,
        TrustPolicy trust,
        IOptions<SocialMeterOptions> options,
        ILogger<SocialMeterService> logger)
    {
        _db = db;
        _titles = titles;
        _sources = sources;
        _assessor = assessor;
        _tracker = tracker;
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
        var sinceRefresh = snapshot is null ? TimeSpan.MaxValue : now - snapshot.LastRefreshedAt;
        var stale = sinceRefresh > TimeSpan.FromHours(_options.RefreshAfterHours);
        var recentlyPulled = sinceRefresh < TimeSpan.FromMinutes(_options.MinForcedRefreshMinutes);

        // The gate that bounds the bill: titles outside the tracked set never trigger a pull.
        var trackedReason = await _tracker.GetReasonAsync(mediaType, tmdbId, ct);
        var tracked = trackedReason is not null;

        var sourceStatus = new List<SourceStatusDto>();
        var fromCache = true;

        if (tracked && (stale || (forceRefresh && !recentlyPulled)))
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

            // When every configured source failed, retry in ten minutes instead of caching the failure for the full window.
            var configured = sourceStatus.Where(s => s.Configured).ToList();
            var allFailed = configured.Count > 0 && configured.All(s => s.Error is not null);
            snapshot.ApplyMeter(meter, allFailed ? now.AddHours(-_options.RefreshAfterHours).AddMinutes(10) : now);

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
            Tracked = tracked,
            TrackedReason = trackedReason,
            Meter = meter,
            TopPosts = top,
            Sources = sourceStatus,
            LastRefreshedAt = snapshot?.LastRefreshedAt ?? now,
            FromCache = fromCache,
            NextRefreshAllowedAt = (snapshot?.LastRefreshedAt ?? now).AddMinutes(_options.MinForcedRefreshMinutes)
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

    public async Task<int> RescoreAsync(bool force, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var posts = await _db.SocialPosts.ToListAsync(ct);
        var assessed = 0;

        foreach (var group in posts.GroupBy(p => (p.MediaType, p.TmdbId)))
        {
            var titlePosts = group.ToList();

            // Seeded demo posts are hand-labelled and never re-assessed.
            var toAssess = titlePosts
                .Where(p => !p.ExternalId.StartsWith("seed-") && (force || p.Analyzer != _assessor.Name))
                .ToList();

            if (toAssess.Count > 0)
            {
                var title = await _titles.GetDetailsAsync(group.Key.MediaType, group.Key.TmdbId, ct)
                            ?? new Title { TmdbId = group.Key.TmdbId, MediaType = group.Key.MediaType, Name = "" };
                var results = await _assessor.AssessAsync(title, toAssess.Select(ToCandidate).ToList(), ct);
                for (var i = 0; i < toAssess.Count; i++)
                    Apply(toAssess[i], results[i], _trust.Evaluate(ToCandidate(toAssess[i]), now));
                assessed += toAssess.Count;
            }

            TrustPolicy.ApplyCrossPostRules(titlePosts, now);
            var meter = SocialScoreCalculator.Compute(titlePosts, now, _options.MomentumWindowDays);

            var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == group.Key.TmdbId && s.MediaType == group.Key.MediaType, ct);
            snapshot?.ApplyMeter(meter, snapshot.LastRefreshedAt);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Rescored {Assessed} of {Total} posts with {Assessor}", assessed, posts.Count, _assessor.Name);
        return assessed;
    }

    /// <summary>Writes an assessment plus trust verdict onto a post. Trust exclusions win; otherwise an off-topic post is excluded.</summary>
    private void Apply(SocialPost post, PostAssessment assessment, TrustVerdict trust)
    {
        var result = assessment.ToResult();
        post.Sentiment = result.Label;
        post.SentimentScore = result.Score;
        post.Analyzer = _assessor.Name;
        post.AnalyzerNote = assessment.Note is null ? null : Trim(assessment.Note, 120);
        post.TrustWeight = trust.Weight;
        // The platform's flag or our own read of the image — either hides media behind a tap.
        post.Sensitive = post.Sensitive || assessment.SensitiveMedia;

        if (trust.Excluded)
        {
            post.Excluded = true;
            post.ExclusionReason = trust.ExclusionReason;
        }
        else if (!assessment.AboutTitle)
        {
            post.Excluded = true;
            post.ExclusionReason = TrustPolicy.ReasonOffTopic;
        }
        else
        {
            post.Excluded = false;
            post.ExclusionReason = null;
        }
    }

    private static SocialPostCandidate ToCandidate(SocialPost p) => new(
        p.Platform, p.ExternalId, p.Author, p.Content, p.Upvotes, p.Comments, p.CreatedAt, p.SourceUrl, p.Community,
        p.AuthorId, p.AuthorCreatedAt, p.AuthorReputation,
        p.MediaJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<List<PostMedia>>(p.MediaJson),
        p.Sensitive);

    private static string? SerializeMedia(IReadOnlyList<PostMedia>? media)
    {
        if (media is null || media.Count == 0) return null;
        var json = System.Text.Json.JsonSerializer.Serialize(media.Take(4));
        return json.Length <= 2000 ? json : System.Text.Json.JsonSerializer.Serialize(media.Take(1));
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

        // Upsert first, then assess only what the current assessor hasn't read — re-reading costs tokens.
        var toAssess = new List<SocialPost>();
        foreach (var c in candidates)
        {
            if (existing.TryGetValue(c.ExternalId, out var post))
            {
                // Engagement, account standing and (edited) text keep moving after a post is first seen; keep them current.
                // Media arriving for a post we only had text for counts as new information worth re-reading.
                var mediaJson = SerializeMedia(c.Media);
                var edited = post.Content != c.Content || (post.MediaJson is null && mediaJson is not null);
                post.Upvotes = c.Upvotes;
                post.Comments = c.Comments;
                post.Content = c.Content;
                post.ContentHash = TrustPolicy.ContentHash(c.Content);
                post.MediaJson = mediaJson ?? post.MediaJson;
                post.Sensitive = c.Sensitive;
                post.AuthorId ??= c.AuthorId;
                post.AuthorCreatedAt ??= c.AuthorCreatedAt;
                post.AuthorReputation = c.AuthorReputation ?? post.AuthorReputation;
                post.FetchedAt = now;

                if (edited || post.Analyzer != _assessor.Name)
                    toAssess.Add(post);
                else
                {
                    var trust = _trust.Evaluate(c, now);
                    post.TrustWeight = trust.Weight;
                    if (trust.Excluded) { post.Excluded = true; post.ExclusionReason = trust.ExclusionReason; }
                    else if (post.ExclusionReason == TrustPolicy.ReasonTooNew || post.ExclusionReason == TrustPolicy.ReasonTooShort) { post.Excluded = false; post.ExclusionReason = null; }
                }
                continue;
            }

            var fresh = new SocialPost
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
                MediaJson = SerializeMedia(c.Media),
                Sensitive = c.Sensitive,
                Upvotes = c.Upvotes,
                Comments = c.Comments,
                CreatedAt = c.CreatedAt,
                FetchedAt = now,
                SourceUrl = c.SourceUrl is null ? null : Trim(c.SourceUrl, 500),
                Community = c.Community is null ? null : Trim(c.Community, 100)
            };
            _db.SocialPosts.Add(fresh);
            toAssess.Add(fresh);
        }

        if (toAssess.Count > 0)
        {
            var assessments = await _assessor.AssessAsync(title, toAssess.Select(ToCandidate).ToList(), ct);
            for (var i = 0; i < toAssess.Count; i++)
                Apply(toAssess[i], assessments[i], _trust.Evaluate(ToCandidate(toAssess[i]), now));
        }

        await _db.SaveChangesAsync(ct);
        return new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = candidates.Count };
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    public static string Key(MediaType mediaType) => mediaType.Segment();
}
