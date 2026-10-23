using System.Text.Json.Serialization;

namespace SocialMeter.Infrastructure.Tmdb.Dtos;

/// <summary>Shape shared by TMDB movie and tv list/detail responses. Movie-only and tv-only fields are nullable.</summary>
public sealed class TmdbTitleItem
{
    public int Id { get; set; }

    /// <summary>Present on multi-search and trending/all results: "movie", "tv" or "person".</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    // movie
    public string? Title { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    public int? Runtime { get; set; }
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }

    // tv
    public string? Name { get; set; }
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; set; }
    [JsonPropertyName("episode_run_time")] public List<int>? EpisodeRunTime { get; set; }
    [JsonPropertyName("number_of_seasons")] public int? NumberOfSeasons { get; set; }
    [JsonPropertyName("number_of_episodes")] public int? NumberOfEpisodes { get; set; }

    // shared
    public string? Overview { get; set; }
    public string? Tagline { get; set; }
    public string? Status { get; set; }
    public string? Homepage { get; set; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; set; }
    public double Popularity { get; set; }
    [JsonPropertyName("genre_ids")] public List<int>? GenreIds { get; set; }
    public List<TmdbGenre>? Genres { get; set; }
    [JsonPropertyName("external_ids")] public TmdbExternalIds? ExternalIds { get; set; }
    public TmdbVideos? Videos { get; set; }
}

public sealed class TmdbVideos
{
    public List<TmdbVideo>? Results { get; set; }
}

public sealed class TmdbVideo
{
    [JsonPropertyName("iso_639_1")] public string? Language { get; set; }
    public string? Key { get; set; }
    public string? Name { get; set; }
    public string? Site { get; set; }
    public string? Type { get; set; }
    public bool Official { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class TmdbGenre
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public sealed class TmdbExternalIds
{
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }
}
