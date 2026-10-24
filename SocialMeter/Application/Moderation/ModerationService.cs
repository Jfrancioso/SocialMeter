using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Common;
using SocialMeter.Application.Social;
using SocialMeter.Contracts.Admin;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Moderation;

/// <summary>
/// Hide, restore and relabel posts by hand, with an audit record, and recompute the title at once from what is stored
/// (no fetching, no model calls, so no cost). Runs under the title's pull gate so it never interleaves with a live pull's save.
/// </summary>
public sealed class ModerationService
{
    private readonly SocialMeterDbContext _db;
    private readonly TitleRecomputer _recomputer;
    private readonly PullGate _gate;
    private readonly ILogger<ModerationService> _logger;

    public ModerationService(SocialMeterDbContext db, TitleRecomputer recomputer, PullGate gate, ILogger<ModerationService> logger)
    {
        _db = db;
        _recomputer = recomputer;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>Null when there is no such post.</summary>
    public Task<ModerationResultDto?> HideAsync(int postId, string reason, string? titleName, string actor, CancellationToken ct = default)
    {
        // From here on only our own copy of the reason is used, so nothing the caller typed reaches the store or the log.
        var known = PostModeration.Canonical(reason)
                    ?? throw new ArgumentException($"Reason must be one of: {string.Join(", ", PostModeration.Reasons)}.", nameof(reason));
        return ApplyAsync(postId, actor, titleName, ModerationAction.Hide, known, (post, now) => PostModeration.Hide(post, known, now), ct);
    }

    /// <summary>Null when there is no such post.</summary>
    public Task<ModerationResultDto?> RestoreAsync(int postId, string actor, CancellationToken ct = default)
        => ApplyAsync(postId, actor, null, ModerationAction.Restore, null, (post, _) => PostModeration.Restore(post), ct);

    /// <summary>
    /// Corrects the post's label by hand, with the operator's reason (<paramref name="note"/>, already checked with
    /// PostModeration.CleanNote). The log keeps the assessor's label from before. Null when there is no such post.
    /// </summary>
    public Task<ModerationResultDto?> RelabelAsync(int postId, Sentiment label, string note, string? titleName, string actor, CancellationToken ct = default)
    {
        string? fromLabel = null;
        double? fromScore = null;
        return ApplyAsync(postId, actor, titleName, ModerationAction.Relabel, null,
            (post, now) =>
            {
                (fromLabel, fromScore) = (Label(post.Sentiment), post.SentimentScore);
                return PostModeration.Relabel(post, label, now);
            },
            ct,
            entry => (entry.FromLabel, entry.FromScore, entry.ToLabel, entry.Note) = (fromLabel, fromScore, Label(label), note));
    }

    /// <summary>
    /// Undoes the latest relabel: the label from before it comes back, and later re-reads may change it again. Null when
    /// there is no such post; Changed is false when its label wasn't fixed.
    /// </summary>
    public async Task<ModerationResultDto?> UndoRelabelAsync(int postId, string actor, CancellationToken ct = default)
    {
        var last = await _db.ModerationActions
            .Where(a => a.PostId == postId && a.Action == ModerationAction.Relabel)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => new { a.FromLabel, a.FromScore })
            .FirstOrDefaultAsync(ct);
        var original = Enum.TryParse<Sentiment>(last?.FromLabel, ignoreCase: true, out var parsed) ? parsed : (Sentiment?)null;
        string? corrected = null;

        return await ApplyAsync(postId, actor, null, ModerationAction.UndoRelabel, null,
            (post, _) =>
            {
                corrected = Label(post.Sentiment);
                return original is not null && PostModeration.UndoRelabel(post, original.Value, last!.FromScore ?? 0);
            },
            ct,
            entry => (entry.FromLabel, entry.ToLabel) = (corrected, last?.FromLabel));
    }

    private static string Label(Sentiment s) => s.ToString().ToLowerInvariant();

    /// <summary>
    /// Runs one moderation step under the title's pull gate (so it never races a live pull of the same title), logs it in
    /// ModerationActions and recomputes the score. <paramref name="change"/> returns false when there was nothing to do;
    /// <paramref name="describe"/> adds a step's own details to its log entry.
    /// </summary>
    private async Task<ModerationResultDto?> ApplyAsync(int postId, string actor, string? titleName, string action, string? reason,
        Func<SocialPost, DateTime, bool> change, CancellationToken ct, Action<ModerationAction>? describe = null)
    {
        var key = await _db.SocialPosts.Where(p => p.Id == postId).Select(p => new { p.MediaType, p.TmdbId }).SingleOrDefaultAsync(ct);
        if (key is null) return null;

        using var gate = await _gate.AcquireAsync(key.MediaType, key.TmdbId, ct);
        var now = DateTime.UtcNow;
        var post = await _db.SocialPosts.SingleOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return null;   // purged while we waited for the gate

        var changed = change(post, now);
        SocialMeterScore? meter = null;
        if (changed)
        {
            var entry = new ModerationAction
            {
                At = now,
                PostId = post.Id,
                MediaType = post.MediaType,
                TmdbId = post.TmdbId,
                TitleName = (titleName ?? await LastTitleNameAsync(post, ct)).Clip(200),
                Action = action,
                Reason = reason,
                Actor = actor.Clip(200)
            };
            describe?.Invoke(entry);
            _db.ModerationActions.Add(entry);
            meter = await _recomputer.RecomputeAsync(post.MediaType, post.TmdbId, now, ct);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Moderation: {Action} post {PostId} ({MediaType} {TmdbId}) reason {Reason}",
                action, post.Id, post.MediaType, post.TmdbId, reason ?? "-");
        }

        var snapshot = meter is null
            ? await _db.TitleSnapshots.Where(s => s.TmdbId == post.TmdbId && s.MediaType == post.MediaType)
                .Select(s => new { s.Score, s.Label }).SingleOrDefaultAsync(ct)
            : new { meter.Score, meter.Label };

        return new ModerationResultDto
        {
            PostId = post.Id,
            Hidden = post.Hidden,
            Sentiment = Label(post.Sentiment),
            LabelFixed = post.LabelFixedAt is not null,
            Changed = changed,
            Score = snapshot?.Score,
            Label = snapshot?.Label,
            Meter = meter
        };
    }

    /// <summary>The newest entries first, with each post as it is now.</summary>
    public async Task<IReadOnlyList<ModerationEntryDto>> RecentAsync(int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var actions = await _db.ModerationActions.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(take).ToListAsync(ct);
        var ids = actions.Select(a => a.PostId).Distinct().ToList();
        var posts = await _db.SocialPosts.Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Platform, p.Author, p.Content, p.SourceUrl, p.HiddenAt, p.LabelFixedAt })
            .ToDictionaryAsync(p => p.Id, ct);

        return actions.Select(a =>
        {
            posts.TryGetValue(a.PostId, out var p);
            return new ModerationEntryDto
            {
                Id = a.Id,
                At = a.At,
                Action = a.Action,
                Reason = a.Reason,
                Actor = a.Actor,
                PostId = a.PostId,
                MediaType = a.MediaType,
                TmdbId = a.TmdbId,
                TitleName = a.TitleName,
                Platform = p?.Platform,
                Author = p?.Author,
                Excerpt = p is null ? null : p.Content.ReplaceLineEndings(" ").Clip(200),
                SourceUrl = p?.SourceUrl,
                CurrentlyHidden = p?.HiddenAt is not null,
                CurrentlyRelabeled = p?.LabelFixedAt is not null,
                FromLabel = a.FromLabel,
                ToLabel = a.ToLabel,
                Note = a.Note,
                PostGone = p is null
            };
        }).ToList();
    }

    // A restore arrives without the title's name; reuse the one the hide recorded.
    private async Task<string> LastTitleNameAsync(SocialPost post, CancellationToken ct) =>
        await _db.ModerationActions.Where(a => a.PostId == post.Id && a.TitleName != "")
            .OrderByDescending(a => a.At).Select(a => a.TitleName).FirstOrDefaultAsync(ct) ?? "";

}
