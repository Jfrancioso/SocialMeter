using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Moderation;

/// <summary>
/// Hand moderation of one post, two kinds:
///
/// Hiding. A hidden post is excluded whatever the filters decide, and stays that way through every later fetch,
/// re-read and rescore until an operator restores it. <see cref="SocialPost.ExclusionReason"/> keeps the filters' own
/// verdict untouched underneath, so a restore puts the post back exactly where the filters had it.
///
/// Relabelling. An operator corrects the assessor's label (negative, neutral, positive) and says why. The corrected
/// label stays through every later re-read (<see cref="WriteAssessedLabel"/> is the only way the assessor's label
/// reaches a post) until an operator undoes it. The assessor's original label and the reason are kept in the moderation
/// log: the record of where it goes wrong, for worked examples in its instructions and for its accuracy test.
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

    // ---- Relabelling ----

    /// <summary>A relabel needs a reason this long, and keeps at most MaxNoteLength of it.</summary>
    public const int MinNoteLength = 3, MaxNoteLength = 300;

    /// <summary>The reason as stored: trimmed and clipped; null when it is too short to say anything.</summary>
    public static string? CleanNote(string? note)
    {
        var trimmed = note?.Trim() ?? "";
        return trimmed.Length < MinNoteLength ? null : trimmed[..Math.Min(trimmed.Length, MaxNoteLength)];
    }

    /// <summary>
    /// The assessor's label, written onto a post unless an operator has fixed it. Every assessor write goes through here,
    /// as every exclusion write keeps <c>Excluded |= Hidden</c>.
    /// </summary>
    public static void WriteAssessedLabel(SocialPost post, Sentiment label, double score)
    {
        if (post.LabelFixedAt is not null) return;

        post.Sentiment = label;
        post.SentimentScore = score;
    }

    /// <summary>
    /// Sets the label by hand. The tone keeps its strength and takes the new side (a "negative -0.6" made positive becomes
    /// +0.6; one with no strength to keep gets 0.5); neutral has no tone. Returns false when the post already had this label
    /// fixed (nothing changes).
    /// </summary>
    public static bool Relabel(SocialPost post, Sentiment label, DateTime nowUtc)
    {
        if (post.LabelFixedAt is not null && post.Sentiment == label) return false;

        var strength = Math.Abs(post.SentimentScore) >= 0.15 ? Math.Min(Math.Abs(post.SentimentScore), 1) : 0.5;
        post.Sentiment = label;
        post.SentimentScore = label switch
        {
            Sentiment.Positive => strength,
            Sentiment.Negative => -strength,
            _ => 0
        };
        post.LabelFixedAt = nowUtc;
        return true;
    }

    /// <summary>Puts back the label from before the relabel and lets re-reads write it again. False when it wasn't fixed.</summary>
    public static bool UndoRelabel(SocialPost post, Sentiment fromLabel, double fromScore)
    {
        if (post.LabelFixedAt is null) return false;

        post.LabelFixedAt = null;
        post.Sentiment = fromLabel;
        post.SentimentScore = fromScore;
        return true;
    }
}
