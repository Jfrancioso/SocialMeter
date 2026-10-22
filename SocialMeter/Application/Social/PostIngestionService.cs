using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Social;

/// <summary>
/// Gets posts from a source into the database with a sentiment and a trust verdict on each: fetch, upsert, assess only
/// what the current assessor has not read (re-reading costs tokens), apply the trust policy. Knows nothing about scores.
/// </summary>
public sealed class PostIngestionService
{
    private readonly SocialMeterDbContext _db;
    private readonly IPostAssessor _assessor;
    private readonly VerdictAssessor _verdicts;
    private readonly TrustPolicy _trust;
    private readonly ILogger<PostIngestionService> _logger;

    public PostIngestionService(SocialMeterDbContext db, IPostAssessor assessor, VerdictAssessor verdicts, TrustPolicy trust, ILogger<PostIngestionService> logger)
    {
        _db = db;
        _assessor = assessor;
        _verdicts = verdicts;
        _trust = trust;
        _logger = logger;
    }

    public string AssessorName => _assessor.Name;

    /// <summary>
    /// Pulls every source for one title and persists the result, as fast as the slowest step rather than the sum of them:
    /// the sources are fetched at the same time (fetching never touches the database), then all new posts from all
    /// sources go to the assessor together, whose batches run concurrently, and everything is saved once. The posts read
    /// and the batch size are the same as pulling one source at a time, so the cost is too (or a little lower, when
    /// partial batches combine). Never throws for a source failure: each status says what happened.
    /// </summary>
    public async Task<List<SourceStatusDto>> IngestAllAsync(IEnumerable<ISocialSource> sources, Title title, DateTime now, CancellationToken ct)
    {
        var list = sources.ToList();
        var fetches = await Task.WhenAll(list.Select(s => FetchAsync(s, title, ct)));

        // The DbContext is not thread-safe, so the upserts run one source at a time; they are quick database work.
        var toAssess = new List<SocialPost>();
        foreach (var (source, candidates, _) in fetches)
        {
            if (candidates is not { Count: > 0 }) continue;

            var externalIds = candidates.Select(c => c.ExternalId).ToList();
            var existing = await _db.SocialPosts
                .Where(p => p.Platform == source.Platform && externalIds.Contains(p.ExternalId))
                .ToDictionaryAsync(p => p.ExternalId, ct);

            foreach (var c in candidates)
            {
                if (existing.TryGetValue(c.ExternalId, out var post))
                {
                    if (UpdateExisting(post, c, now)) toAssess.Add(post);
                    continue;
                }

                var fresh = NewPost(title, c, now);
                _db.SocialPosts.Add(fresh);
                toAssess.Add(fresh);
            }
        }

        if (toAssess.Count > 0)
            await AssessAndApplyAsync(title, toAssess, now, ct);

        await _db.SaveChangesAsync(ct);
        return fetches.Select(f => f.Status).ToList();
    }

    /// <summary>Pulls one source for one title and persists the result.</summary>
    public async Task<SourceStatusDto> IngestAsync(ISocialSource source, Title title, DateTime now, CancellationToken ct)
        => (await IngestAllAsync(new[] { source }, title, now, ct))[0];

    private async Task<(ISocialSource Source, IReadOnlyList<SocialPostCandidate>? Candidates, SourceStatusDto Status)> FetchAsync(
        ISocialSource source, Title title, CancellationToken ct)
    {
        if (!source.IsConfigured)
            return (source, null, new SourceStatusDto { Platform = source.Platform, Configured = false });

        try
        {
            var candidates = await source.FetchAsync(title, ct);
            return (source, candidates, new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = candidates.Count });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "{Platform} fetch failed for {Title}", source.Platform, title.Name);
            return (source, null, new SourceStatusDto { Platform = source.Platform, Configured = true, Fetched = 0, Error = ex.Message });
        }
    }

    /// <summary>Posts with an explicit platform verdict are scored by it; everything else goes to the configured assessor.</summary>
    public async Task AssessAndApplyAsync(Title title, List<SocialPost> posts, DateTime now, CancellationToken ct)
    {
        foreach (var p in posts.Where(p => p.VerdictUp is not null))
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

    /// <summary>Which assessor should have read this post: the platform verdict for thumbs posts, the configured one otherwise.</summary>
    public string AnalyzerFor(SocialPost p) => p.VerdictUp is null ? _assessor.Name : VerdictAssessor.AnalyzerName;

    /// <summary>Engagement, account standing and edited text keep moving after a post is first seen. Returns true when the post needs re-reading.</summary>
    private bool UpdateExisting(SocialPost post, SocialPostCandidate c, DateTime now)
    {
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
            return true;

        // Not re-read, but the account may have aged into or out of the trust rules since last time.
        var trust = _trust.Evaluate(c, now);
        post.TrustWeight = trust.Weight;
        if (trust.Excluded) { post.Excluded = true; post.ExclusionReason = trust.ExclusionReason; }
        else if (post.ExclusionReason is TrustPolicy.ReasonTooNew or TrustPolicy.ReasonTooShort) { post.Excluded = false; post.ExclusionReason = null; }
        return false;
    }

    private static SocialPost NewPost(Title title, SocialPostCandidate c, DateTime now) => new()
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

    /// <summary>Writes an assessment plus trust verdict onto a post. Trust exclusions win; otherwise an off-topic post is excluded.</summary>
    private static void Apply(SocialPost post, PostAssessment assessment, TrustVerdict trust, string analyzer)
    {
        var result = assessment.ToResult();
        post.Sentiment = result.Label;
        post.SentimentScore = result.Score;
        post.Analyzer = analyzer;
        post.AnalyzerNote = assessment.Note is null ? null : Trim(assessment.Note, 120);
        post.Substance = Math.Clamp(assessment.Substance, 0, 1);
        post.TrustWeight = trust.Weight;
        // The platform's flag or our own read of the image: either hides media behind a tap.
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
        p.MediaJson is null ? null : JsonSerializer.Deserialize<List<PostMedia>>(p.MediaJson),
        p.Sensitive,
        p.VerdictUp, p.EngagementMinutes);

    private static string? SerializeMedia(IReadOnlyList<PostMedia>? media)
    {
        if (media is null || media.Count == 0) return null;
        var json = JsonSerializer.Serialize(media.Take(4));
        return json.Length <= 2000 ? json : JsonSerializer.Serialize(media.Take(1));
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
