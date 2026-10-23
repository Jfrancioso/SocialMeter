namespace SocialMeter.Options;

/// <summary>
/// Per-visitor request ceilings inside the API, counted per client address (Cloudflare's CF-Connecting-IP). They are a
/// backstop, set high enough that ordinary browsing never meets them, including many people behind one address (mobile
/// carriers and offices share IPs). Abuse is stopped earlier and elsewhere: Cloudflare's edge rule blocks more than 50
/// requests in 10 s from one visitor, and LivePullBudget caps the live pulls that cost money.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>All API requests from one address per minute. Matches Cloudflare's edge limit (50 per 10 s).</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>Requests per minute to the bulk-read endpoints (post feed, explanations, history, card badges, search).</summary>
    public int ReadsPerMinute { get; set; } = 180;
}
