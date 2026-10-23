using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Social;

public sealed class SocialMeterResponseDto
{
    public int TmdbId { get; init; }
    public MediaType MediaType { get; init; }
    public string TitleName { get; init; } = "";

    /// <summary>False when the title is outside the tracked set: no pulls happen and any reading shown is a leftover from when it was tracked.</summary>
    public bool Tracked { get; init; }
    public string? TrackedReason { get; init; }

    public SocialMeterScore Meter { get; init; } = new();

    /// <summary>The most influential posts behind the score.</summary>
    public IReadOnlyList<SocialPostDto> TopPosts { get; init; } = Array.Empty<SocialPostDto>();

    public IReadOnlyList<SourceStatusDto> Sources { get; init; } = Array.Empty<SourceStatusDto>();

    /// <summary>When the sources were last read for this title; null when they never have been.</summary>
    public DateTime? LastRefreshedAt { get; init; }

    /// <summary>
    /// True when a read was due but the hourly ceiling on live reads turned it away. The page says so instead of
    /// presenting the old (or empty) reading as fresh; reads resume within <see cref="NextRefreshAllowedAt"/>.
    /// </summary>
    public bool ReadsPaused { get; init; }

    /// <summary>True when this reading came straight from the cache without contacting the sources.</summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// Earliest time a forced refresh will contact the sources again: the cool-down after a read (see
    /// SocialMeter:MinForcedRefreshMinutes), or the budget's next refill while reads are paused. Null when nothing blocks one.
    /// </summary>
    public DateTime? NextRefreshAllowedAt { get; init; }
}

public sealed class SocialPostDto
{
    public int Id { get; init; }
    public string Platform { get; init; } = "";
    public string Author { get; init; } = "";
    public string Content { get; init; } = "";
    public Sentiment Sentiment { get; init; }
    public double SentimentScore { get; init; }
    public int Upvotes { get; init; }
    public int Comments { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? SourceUrl { get; init; }
    public string? Community { get; init; }

    /// <summary>True when the author is an established account counting at full weight. The weight itself is never exposed.</summary>
    public bool Established { get; init; }

    public IReadOnlyList<PostMedia> Media { get; init; } = Array.Empty<PostMedia>();

    /// <summary>The platform flagged the post as possibly sensitive; show media only on request.</summary>
    public bool Sensitive { get; init; }

    /// <summary>Steam's recommend / don't-recommend; null for ordinary posts.</summary>
    public bool? VerdictUp { get; init; }

    /// <summary>True when the post carries real substance (specifics, an argument) rather than a bare reaction.</summary>
    public bool Detailed { get; init; }

    /// <summary>Hours played at the time of the review; null when unknown.</summary>
    public double? PlaytimeHours { get; init; }

    /// <summary>"bare" (a reaction), "some" (one general reason) or "detailed" (specifics, an argument).</summary>
    public string SubstanceLevel { get; init; } = "";

    /// <summary>"full" or "reduced": whether the trust policy discounted the author. The amount is never exposed.</summary>
    public string TrustLevel { get; init; } = "";

    /// <summary>This post's share of the title's total weight, 0–100; null when not computed for this listing.</summary>
    public double? WeightShare { get; init; }

    public static string SubstanceLevelFor(double substance) => substance >= 0.6 ? "detailed" : substance >= 0.3 ? "some" : "bare";
    public static string TrustLevelFor(double trust) => trust >= 1 ? "full" : "reduced";

    public static SocialPostDto From(SocialPost p, double? weightShare = null) => new()
    {
        VerdictUp = p.VerdictUp,
        Detailed = p.Substance >= 0.6,
        SubstanceLevel = SubstanceLevelFor(p.Substance),
        TrustLevel = TrustLevelFor(p.TrustWeight),
        WeightShare = weightShare is null ? null : Math.Round(weightShare.Value, 1),
        PlaytimeHours = p.EngagementMinutes is { } m ? Math.Round(m / 60.0, 1) : null,
        Id = p.Id,
        Platform = p.Platform,
        Author = p.Author,
        Content = p.Content,
        Media = p.MediaJson is null ? Array.Empty<PostMedia>() : System.Text.Json.JsonSerializer.Deserialize<List<PostMedia>>(p.MediaJson) ?? new List<PostMedia>(),
        Sensitive = p.Sensitive,
        Sentiment = p.Sentiment,
        SentimentScore = Math.Round(p.SentimentScore, 2),
        Established = p.TrustWeight >= 1,
        Upvotes = p.Upvotes,
        Comments = p.Comments,
        CreatedAt = p.CreatedAt,
        SourceUrl = p.SourceUrl,
        Community = p.Community
    };
}

public sealed class SourceStatusDto
{
    public string Platform { get; init; } = "";
    public bool Configured { get; init; }

    /// <summary>Posts this source contributed on the last refresh; null when it was not contacted.</summary>
    public int? Fetched { get; init; }

    public string? Error { get; init; }
}

public sealed class SnapshotDto
{
    public int Score { get; init; }
    public string Label { get; init; } = "";
    public int TotalPosts { get; init; }
    public double Confidence { get; init; }

    /// <summary>95 % half-width in points.</summary>
    public int Margin { get; init; }

    /// <summary>The critics' score for movies and shows (Tomatometer and Metascore averaged), when known.</summary>
    public int? CriticsScore { get; init; }
}
