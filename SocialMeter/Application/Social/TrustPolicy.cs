using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Application.Social;

/// <summary>
/// Decides how much a post is allowed to move the meter, and which posts don't count at all.
/// Two layers: per-post signals (account age, reputation, content quality) evaluated here, and
/// cross-post rules (one voice per account, copy-paste duplicates) applied over a title's whole set
/// in <see cref="ApplyCrossPostRules"/>.
/// </summary>
public sealed partial class TrustPolicy
{
    public const string ReasonTooNew = "account too new";
    public const string ReasonTooShort = "content too short";
    public const string ReasonSameAccount = "additional post from the same account";
    public const string ReasonDuplicate = "duplicate of an earlier post";
    public const string ReasonOffTopic = "not about this title";
    public const string ReasonTooLittlePlaytime = "too little playtime";

    private static readonly HashSet<string> CrossPostReasons = new() { ReasonSameAccount, ReasonDuplicate };

    private readonly SocialMeterOptions _o;

    public TrustPolicy(IOptions<SocialMeterOptions> options)
    {
        _o = options.Value;
    }

    public TrustVerdict Evaluate(SocialPostCandidate c, DateTime nowUtc)
    {
        // "10/10" with a thumbs-up is a complete review; the length rule is for free-text posts without a verdict.
        if (c.VerdictUp is null && Normalize(c.Content).Length < _o.MinContentLength)
            return TrustVerdict.Exclude(ReasonTooShort);

        var weight = 1.0;

        if (c.EngagementMinutes is { } played)
        {
            // Playtime is a far better "is this a real player" signal than account age or reputation
            // (Steam reports 0 games owned for private profiles), so it replaces both.
            if (played < _o.MinPlaytimeMinutes)
                return TrustVerdict.Exclude(ReasonTooLittlePlaytime);

            if (played < _o.FullPlaytimeMinutes)
            {
                var t = (played - _o.MinPlaytimeMinutes) / (double)Math.Max(1, _o.FullPlaytimeMinutes - _o.MinPlaytimeMinutes);
                weight *= 0.5 + 0.5 * Math.Clamp(t, 0, 1);
            }

            if (c.VerifiedOwner == false) weight *= 0.7;
            if (c.ReceivedForFree) weight *= 0.6;

            return new TrustVerdict(Math.Round(Math.Clamp(weight, 0.05, 1), 3), null);
        }

        if (c.AuthorCreatedAt is { } created)
        {
            var ageDays = (nowUtc - created).TotalDays;
            if (ageDays < _o.MinAccountAgeDays)
                return TrustVerdict.Exclude(ReasonTooNew);

            // Ramp from 0.4 at the minimum age to 1.0 once established.
            if (ageDays < _o.EstablishedAccountDays)
            {
                var t = (ageDays - _o.MinAccountAgeDays) / Math.Max(1, _o.EstablishedAccountDays - _o.MinAccountAgeDays);
                weight *= 0.4 + 0.6 * Math.Clamp(t, 0, 1);
            }
        }
        else
        {
            weight *= _o.UnknownAccountWeight;
        }

        if (c.AuthorReputation is { } rep && rep < _o.MinReputation)
            weight *= 0.5;

        if (LinkRegex().Matches(c.Content).Count >= _o.LinkSpamThreshold)
            weight *= 0.5;

        return new TrustVerdict(Math.Round(Math.Clamp(weight, 0.05, 1), 3), null);
    }

    /// <summary>
    /// Re-evaluates the rules that depend on the other posts about the same title. Safe to run repeatedly:
    /// it clears only its own exclusions before deciding again.
    /// </summary>
    public static void ApplyCrossPostRules(IReadOnlyCollection<SocialPost> posts, DateTime nowUtc)
    {
        foreach (var p in posts.Where(p => p.Excluded && p.ExclusionReason is not null && CrossPostReasons.Contains(p.ExclusionReason)))
        {
            p.Excluded = false;
            p.ExclusionReason = null;
        }

        var live = posts.Where(p => !p.Excluded).ToList();

        // One voice per account per title: the post the community engaged with most is the one that counts.
        foreach (var group in live.Where(p => p.AuthorId is not null).GroupBy(p => (p.Platform, p.AuthorId)))
        {
            foreach (var extra in group.OrderByDescending(p => SocialScoreCalculator.Engagement(p)).Skip(1))
            {
                extra.Excluded = true;
                extra.ExclusionReason = ReasonSameAccount;
            }
        }

        // Copy-paste campaigns: identical text from different accounts counts once, for whoever said it first.
        // Short verdict reviews ("10/10", "peak") from different players are the same words, not a campaign.
        foreach (var group in live
                     .Where(p => !p.Excluded && p.ContentHash.Length > 0 && !(p.VerdictUp is not null && Normalize(p.Content).Length < 40))
                     .GroupBy(p => p.ContentHash))
        {
            foreach (var dup in group.OrderBy(p => p.CreatedAt).Skip(1))
            {
                dup.Excluded = true;
                dup.ExclusionReason = ReasonDuplicate;
            }
        }
    }

    /// <summary>Lower-cased, punctuation-stripped, whitespace-collapsed text so trivially altered copies still match.</summary>
    public static string Normalize(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        var lowered = LinkRegex().Replace(content, " ").ToLowerInvariant();
        var stripped = NonWordRegex().Replace(lowered, " ");
        return WhitespaceRegex().Replace(stripped, " ").Trim();
    }

    public static string ContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(content)));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public readonly record struct TrustVerdict(double Weight, string? ExclusionReason)
{
    public bool Excluded => ExclusionReason is not null;

    public static TrustVerdict Exclude(string reason) => new(0, reason);
}
