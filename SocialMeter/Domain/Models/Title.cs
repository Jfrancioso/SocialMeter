namespace SocialMeter.Domain.Models;

/// <summary>A movie or TV show as SocialMeter knows it (sourced from TMDB).</summary>
public sealed class Title
{
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

    public int? Year => ReleaseDate?.Year;
}
