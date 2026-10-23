using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Social;
using SocialMeter.Contracts.Admin;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;

namespace SocialMeter.Application.Moderation;

/// <summary>
/// Hide and restore posts by hand, with an audit record, and recompute the title at once from what is stored (no
/// fetching, no model calls, so no cost). Runs under the title's pull gate so it never interleaves with a live pull's save.
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
        if (!PostModeration.IsValidReason(reason))
            throw new ArgumentException($"Reason must be one of: {string.Join(", ", PostModeration.Reasons)}.", nameof(reason));
        return ApplyAsync(postId, actor, titleName, ct, (post, now) => PostModeration.Hide(post, reason, now), "hide", reason);
    }

    /// <summary>Null when there is no such post.</summary>
    public Task<ModerationResultDto?> RestoreAsync(int postId, string actor, CancellationToken ct = default)
        => ApplyAsync(postId, actor, null, ct, (post, _) => PostModeration.Restore(post), "restore", null);

    private async Task<ModerationResultDto?> ApplyAsync(int postId, string actor, string? titleName, CancellationToken ct,
        Func<SocialPost, DateTime, bool> change, string action, string? reason)
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
            _db.ModerationActions.Add(new ModerationAction
            {
                At = now,
                PostId = post.Id,
                MediaType = post.MediaType,
                TmdbId = post.TmdbId,
                TitleName = Trim(titleName ?? await LastTitleNameAsync(post, ct), 200),
                Action = action,
                Reason = reason,
                Actor = Trim(actor, 200)
            });
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
            .Select(p => new { p.Id, p.Platform, p.Author, p.Content, p.SourceUrl, p.HiddenAt })
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
                Excerpt = p is null ? null : Trim(p.Content.ReplaceLineEndings(" "), 200),
                SourceUrl = p?.SourceUrl,
                CurrentlyHidden = p?.HiddenAt is not null,
                PostGone = p is null
            };
        }).ToList();
    }

    // A restore arrives without the title's name; reuse the one the hide recorded.
    private async Task<string> LastTitleNameAsync(SocialPost post, CancellationToken ct) =>
        await _db.ModerationActions.Where(a => a.PostId == post.Id && a.TitleName != "")
            .OrderByDescending(a => a.At).Select(a => a.TitleName).FirstOrDefaultAsync(ct) ?? "";

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
