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
    // One live pull per title at a time. Two tabs opening a stale title together must not each pay for a pull;
    // the second waits, re-reads the snapshot the first wrote, and serves it from cache.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(MediaType, int), SemaphoreSlim> PullLocks = new();

    private readonly SocialMeterDbContext _db;
    private readonly ITitleService _titles;
    private readonly IEnumerable<ISocialSource> _sources;
    private readonly IPostAssessor _assessor;
    private readonly VerdictAssessor _verdicts;
    private readonly ITitleTracker _tracker;
    private readonly TrustPolicy _trust;
    private readonly ScoreHistoryService _history;
    private readonly LivePullBudget _budget;
    private readonly SocialMeterOptions _options;
    private readonly ILogger<SocialMeterService> _logger;

    // Rescore walks every stored post; two at once would double the model bill and race on the same rows.
    private static readonly SemaphoreSlim RescoreLock = new(1, 1);

    public SocialMeterService(
        SocialMeterDbContext db,
        ITitleService titles,
        IEnumerable<ISocialSource> sources,
        IPostAssessor assessor,
        VerdictAssessor verdicts,
        ITitleTracker tracker,
        TrustPolicy trust,
        ScoreHistoryService history,
        LivePullBudget budget,
        IOptions<SocialMeterOptions> options,
        ILogger<SocialMeterService> logger)
    {
        _db = db;
        _titles = titles;
        _sources = sources;
        _assessor = assessor;
        _verdicts = verdicts;
        _tracker = tracker;
        _trust = trust;
        _history = history;
        _budget = budget;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SocialMeterResponseDto?> GetScoreAsync(MediaType mediaType, int tmdbId, bool forceRefresh, CancellationToken ct = default)
    {
        var title = await _titles.GetDetailsAsync(mediaType, tmdbId, ct);
        if (title is null)
            return null;

        // The gate that bounds the bill: titles outside the tracked set never trigger a pull.
        var trackedReason = await _tracker.GetReasonAsync(mediaType, tmdbId, ct);
        var tracked = trackedReason is not null;

        var sourceStatus = new List<SourceStatusDto>();
        var fromCache = true;
        var now = DateTime.UtcNow;
        TitleSnapshot? snapshot;

        var gate = PullLocks.GetOrAdd((mediaType, tmdbId), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Read the snapshot inside the lock: a request that waited here sees what the previous one just wrote.
            snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == tmdbId && s.MediaType == mediaType, ct);
            var sinceRefresh = snapshot is null ? TimeSpan.MaxValue : now - snapshot.LastRefreshedAt;
            var stale = sinceRefresh > TimeSpan.FromHours(_options.RefreshAfterHours);
            var recentlyPulled = sinceRefresh < TimeSpan.FromMinutes(_options.MinForcedRefreshMinutes);

            var wantsPull = tracked && (stale || (forceRefresh && !recentlyPulled));
            if (wantsPull && !_budget.TryAcquirePull())
            {
                // The hourly ceiling is the last line against a runaway bill; the reader sees the cache and the reason.
                _logger.LogWarning("Live pull budget exhausted; serving cached reading for {Type} {Id}", mediaType, tmdbId);
                wantsPull = false;
                sourceStatus.AddRange(_sources.Select(s => new SourceStatusDto { Platform = s.Platform, Configured = s.IsConfigured, Error = s.IsConfigured ? "Live reads paused: hourly budget reached" : null }));
            }
            else if (!wantsPull)
            {
                sourceStatus.AddRange(_sources.Select(s => new SourceStatusDto { Platform = s.Platform, Configured = s.IsConfigured }));
            }

            if (wantsPull)
            {
                fromCache = false;
                foreach (var source in _sources)
                    sourceStatus.Add(await IngestAsync(source, title, now, ct));
            }
        }
        finally
        {
            if (fromCache) gate.Release();
        }

        try
        {

        var posts = await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType)
            .ToListAsync(ct);

        if (!fromCache)
        {
            // Rules that look across the whole set (one voice per account, duplicates) run once everything is in.
            TrustPolicy.ApplyCrossPostRules(posts, now);
        }

        var meter = SocialScoreCalculator.Compute(posts, now, _options.Scoring);

        if (!fromCache)
        {
            snapshot ??= _db.TitleSnapshots.Add(new TitleSnapshot { TmdbId = tmdbId, MediaType = mediaType }).Entity;

            // When every configured source failed, retry in ten minutes instead of caching the failure for the full window.
            var configured = sourceStatus.Where(s => s.Configured).ToList();
            var allFailed = configured.Count > 0 && configured.All(s => s.Error is not null);
            snapshot.ApplyMeter(meter, allFailed ? now.AddHours(-_options.RefreshAfterHours).AddMinutes(10) : now);
            await _history.RecordAsync(mediaType, tmdbId, meter, now, ct);

            await _db.SaveChangesAsync(ct);
        }
        else if (snapshot is not null && (snapshot.Score != meter.Score || snapshot.Margin != meter.Stats.Margin || snapshot.TotalPosts != meter.TotalPosts))
        {
            // The maths changed (or posts were re-read) since the snapshot was written: bring the cache and today's history
            // row in line with what the page is about to show, without pretending the sources were contacted.
            snapshot.ApplyMeter(meter, snapshot.LastRefreshedAt);
            await _history.RecordAsync(mediaType, tmdbId, meter, now, ct);
            await _db.SaveChangesAsync(ct);
        }

        var counted = posts.Where(p => !p.Excluded).ToList();
        var totalWeight = counted.Sum(p => SocialScoreCalculator.Weight(p, now));
        var top = counted
            .OrderByDescending(p => SocialScoreCalculator.Weight(p, now))
            .Take(_options.TopPosts)
            .Select(p => SocialPostDto.From(p, Share(p, totalWeight, now)))
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
        finally
        {
            // A pull holds the lock until its snapshot is saved, so a waiting request reads the fresh one.
            if (!fromCache) gate.Release();
        }
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
            items.Select(p => SocialPostDto.From(p, Share(p, totalWeight, now))).ToList(),
            page, (int)Math.Ceiling(total / (double)pageSize), total);
    }

    private static double? Share(SocialPost p, double totalWeight, DateTime now)
        => totalWeight > 0 ? 100 * SocialScoreCalculator.Weight(p, now) / totalWeight : null;

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
                Excerpt = Trim(p.Content.ReplaceLineEndings(" "), 140),
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
            .GroupBy(p => p.ExclusionReason ?? "set aside")
            .Select(g => new ReasonCountDto { Reason = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ToList();

        var notes = new List<string>();
        var k = _options.PriorStrength;
        if (counted.Count > 0 && meter.Stats.EffectivePosts < k)
            notes.Add($"Fewer than {k:0} effective posts, so the neutral prior still carries {1 - meter.Confidence:P0} of the score.");
        if (counted.Count > 0 && meter.Stats.PriorPull != 0)
            notes.Add($"The raw weighted mean is {meter.Stats.RawScore}; the pull toward neutral moved it {Math.Abs(meter.Stats.PriorPull)} point{(Math.Abs(meter.Stats.PriorPull) == 1 ? "" : "s")} {(meter.Stats.PriorPull > 0 ? "up" : "down")}.");
        if (drivers.Count > 0 && drivers[0].WeightShare >= 20)
            notes.Add($"One post carries {drivers[0].WeightShare:0}% of the weight.");
        var detailed = mix.Single(m => m.Level == "detailed");
        if (counted.Count > 0)
            notes.Add($"Posts with specifics are {detailed.Posts} of {counted.Count} but carry {detailed.WeightShare:0}% of the weight.");
        if (platforms.Count > 1 && platforms.Max(p => p.Score) - platforms.Min(p => p.Score) >= 15)
            notes.Add($"Platforms disagree: {string.Join(", ", platforms.Select(p => $"{PlatformName(p.Platform)} {p.Score}"))}.");

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
            Notes = notes
        };
    }

    public Task<ScoreHistoryDto> GetHistoryAsync(MediaType mediaType, int tmdbId, int days, CancellationToken ct = default)
        => _history.GetAsync(mediaType, tmdbId, days, DateTime.UtcNow, ct);

    private static string PlatformName(string platform) => platform switch { "x" => "X", "reddit" => "Reddit", "steam" => "Steam", _ => platform };

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
                    Margin = s.Margin
                });
    }

    public async Task<int> RescoreAsync(bool force, CancellationToken ct = default)
    {
        if (!await RescoreLock.WaitAsync(0, ct))
            throw new Common.OperationInProgressException("Rescore");
        try
        {
            return await RescoreCoreAsync(force, ct);
        }
        finally
        {
            RescoreLock.Release();
        }
    }

    private async Task<int> RescoreCoreAsync(bool force, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var posts = await _db.SocialPosts.ToListAsync(ct);
        var assessed = 0;

        foreach (var group in posts.GroupBy(p => (p.MediaType, p.TmdbId)))
        {
            var titlePosts = group.ToList();

            var toAssess = titlePosts
                .Where(p => force || p.Analyzer != AnalyzerFor(p))
                .ToList();

            if (toAssess.Count > 0)
            {
                var title = await _titles.GetDetailsAsync(group.Key.MediaType, group.Key.TmdbId, ct)
                            ?? new Title { TmdbId = group.Key.TmdbId, MediaType = group.Key.MediaType, Name = "" };
                await AssessAndApplyAsync(title, toAssess, now, ct);
                assessed += toAssess.Count;
            }

            TrustPolicy.ApplyCrossPostRules(titlePosts, now);
            var meter = SocialScoreCalculator.Compute(titlePosts, now, _options.Scoring);

            var snapshot = await _db.TitleSnapshots.SingleOrDefaultAsync(s => s.TmdbId == group.Key.TmdbId && s.MediaType == group.Key.MediaType, ct);
            if (snapshot is not null)
            {
                snapshot.ApplyMeter(meter, snapshot.LastRefreshedAt);
                await _history.RecordAsync(group.Key.MediaType, group.Key.TmdbId, meter, now, ct);
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Rescored {Assessed} of {Total} posts with {Assessor}", assessed, posts.Count, _assessor.Name);
        return assessed;
    }

    /// <summary>Posts with an explicit platform verdict are scored by it; everything else goes to the configured assessor.</summary>
    private async Task AssessAndApplyAsync(Title title, List<SocialPost> posts, DateTime now, CancellationToken ct)
    {
        var withVerdict = posts.Where(p => p.VerdictUp is not null).ToList();
        foreach (var p in withVerdict)
        {
            var c = ToCandidate(p);
            Apply(p, _verdicts.Assess(c), _trust.Evaluate(c, now), VerdictAssessor.AnalyzerName);
        }

        var freeText = posts.Where(p => p.VerdictUp is null).ToList();
        if (freeText.Count == 0) return;

        var results = await _assessor.AssessAsync(title, freeText.Select(ToCandidate).ToList(), ct);
        for (var i = 0; i < freeText.Count; i++)
            Apply(freeText[i], results[i], _trust.Evaluate(ToCandidate(freeText[i]), now), _assessor.Name);
    }

    private string AnalyzerFor(SocialPost p) => p.VerdictUp is null ? _assessor.Name : VerdictAssessor.AnalyzerName;

    /// <summary>Writes an assessment plus trust verdict onto a post. Trust exclusions win; otherwise an off-topic post is excluded.</summary>
    private void Apply(SocialPost post, PostAssessment assessment, TrustVerdict trust, string analyzer)
    {
        var result = assessment.ToResult();
        post.Sentiment = result.Label;
        post.SentimentScore = result.Score;
        post.Analyzer = analyzer;
        post.AnalyzerNote = assessment.Note is null ? null : Trim(assessment.Note, 120);
        post.Substance = Math.Clamp(assessment.Substance, 0, 1);
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
        p.Sensitive,
        p.VerdictUp, p.EngagementMinutes);

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
                // Sticky: once the platform or our own read of the image flagged it, a later fetch without the flag must not un-hide it.
                post.Sensitive = post.Sensitive || c.Sensitive;
                post.AuthorId ??= c.AuthorId;
                post.AuthorCreatedAt ??= c.AuthorCreatedAt;
                post.AuthorReputation = c.AuthorReputation ?? post.AuthorReputation;
                post.VerdictUp = c.VerdictUp ?? post.VerdictUp;
                post.EngagementMinutes = c.EngagementMinutes ?? post.EngagementMinutes;
                post.FetchedAt = now;

                if (edited || post.Analyzer != AnalyzerFor(post))
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
                VerdictUp = c.VerdictUp,
                EngagementMinutes = c.EngagementMinutes,
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
            await AssessAndApplyAsync(title, toAssess, now, ct);

        await _db.SaveChangesAsync(ct);
        return new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = candidates.Count };
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    public static string Key(MediaType mediaType) => mediaType.Segment();
}
