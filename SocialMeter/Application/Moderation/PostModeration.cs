using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Moderation;

/// <summary>
/// Hiding a post by hand. A hidden post is excluded whatever the filters decide, and stays that way through every later
/// fetch, re-read and rescore until an operator restores it. <see cref="SocialPost.ExclusionReason"/> keeps the filters'
/// own verdict untouched underneath, so a restore puts the post back exactly where the filters had it.
/// </summary>
public static class PostModeration
{
    /// <summary>The reasons an operator can pick, as stored and shown in the log.</summary>
    public static readonly IReadOnlyList<string> Reasons = new[] { "sensitive", "spam", "off-topic", "wrong-title", "other" };

    /// <summary>How a hidden post is described in the public "set aside" counts.</summary>
    public const string PublicReason = "removed by a moderator";

    public static bool IsValidReason(string? reason) => Canonical(reason) is not null;

    /// <summary>The matching entry of <see cref="Reasons"/> (our own string, not the caller's), or null.</summary>
    public static string? Canonical(string? reason) => Reasons.FirstOrDefault(r => r == reason);

    /// <summary>Hides the post. Returns false when it was already hidden (nothing changes).</summary>
    public static bool Hide(SocialPost post, string reason, DateTime nowUtc)
    {
        if (!IsValidReason(reason)) throw new ArgumentException($"Unknown reason '{reason}'.", nameof(reason));
        if (post.Hidden) return false;

        post.HiddenAt = nowUtc;
        post.HiddenReason = reason;
        post.Excluded = true;
        return true;
    }

    /// <summary>Restores the post to the filters' verdict. Returns false when it was not hidden.</summary>
    public static bool Restore(SocialPost post)
    {
        if (!post.Hidden) return false;

        post.HiddenAt = null;
        post.HiddenReason = null;
        post.Excluded = post.ExclusionReason is not null;
        return true;
    }
}
