namespace SocialMeter.Domain.Models;

/// <summary>A movie, TV show or video game as SocialMeter knows it (movies/shows from TMDB, games from Steam).</summary>
public sealed class Title
{
    /// <summary>The provider's id: TMDB id for movies and shows, Steam app id for games. The name predates games.</summary>
    public int TmdbId { get; init; }
    public MediaType MediaType { get; init; }
    public string Name { get; init; } = "";
    public string Overview { get; init; } = "";
    public string? Tagline { get; init; }
    public string PosterPath { get; init; } = "";
    public string BackdropPath { get; init; } = "";
    public DateOnly? ReleaseDate { get; init; }
    public double VoteAverage { get; init; }
    public int VoteCount { get; init; }
    public double Popularity { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    // Detail-only fields (null on list results)
    public int? RuntimeMinutes { get; init; }
    public int? SeasonCount { get; init; }
    public int? EpisodeCount { get; init; }
    public string? Status { get; init; }
    public string? ImdbId { get; init; }
    public string? Homepage { get; init; }

    /// <summary>The trailer to offer on the title page: YouTube for movies and shows, Steam's own video for games.</summary>
    public TitleTrailer? Trailer { get; init; }

    /// <summary>A show's creators or a movie's directors (details only). Part of what pins down a title whose name is an everyday word.</summary>
    public IReadOnlyList<string> Creators { get; init; } = Array.Empty<string>();

    /// <summary>The top-billed cast and the characters they play (details only).</summary>
    public IReadOnlyList<CastCredit> Cast { get; init; } = Array.Empty<CastCredit>();

    // Game-only fields (null for movies and shows)
    public string? Developer { get; init; }

    /// <summary>Steam's landscape header — the one image every game has; cards use it when the portrait is missing.</summary>
    public string? HeaderImage { get; init; }
    public IReadOnlyList<string> Platforms { get; init; } = Array.Empty<string>();
    public int? MetacriticScore { get; init; }
    public string? MetacriticUrl { get; init; }

    /// <summary>Steam's all-time review summary: percent positive, its label ("Very Positive"), and how many reviews.</summary>
    public int? SteamReviewPercent { get; init; }
    public string? SteamReviewLabel { get; init; }
    public int? SteamReviewCount { get; init; }

    public int? Year => ReleaseDate?.Year;

    // Set by the tracker when the title is served to the UI; not part of TMDB's data.
    /// <summary>True when SocialMeter is measuring this title right now. Locked titles show no meter and trigger no pulls.</summary>
    public bool Tracked { get; set; }
    public string? TrackedReason { get; set; }
}

/// <summary>One actor and the character they play.</summary>
public sealed record CastCredit(string Name, string? Character);
