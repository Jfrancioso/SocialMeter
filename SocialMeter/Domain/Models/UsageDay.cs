namespace SocialMeter.Domain.Models;

/// <summary>
/// One UTC day of paid usage: live pulls, Claude tokens and X items read, plus the cost estimated at the prices configured
/// when the row was written. UsageMeter keeps today's numbers in memory and saves them here about once a minute, so the
/// spend guard and the daily cap survive a restart, and the admin page can show the last few days.
/// </summary>
public sealed class UsageDay
{
    /// <summary>The UTC day; the key.</summary>
    public DateOnly Day { get; set; }

    public int LivePulls { get; set; }
    public long ClaudeInputTokens { get; set; }
    public long ClaudeOutputTokens { get; set; }

    /// <summary>Posts X returned, from searches and from the deletion sync's lookups. X bills each one.</summary>
    public int XPostsRead { get; set; }

    /// <summary>Author profiles X returned alongside searched posts.</summary>
    public int XUsersRead { get; set; }

    public decimal EstimatedUsd { get; set; }

    public DateTime UpdatedAt { get; set; }
}
