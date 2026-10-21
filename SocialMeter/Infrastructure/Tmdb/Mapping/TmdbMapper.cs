using SocialMeter.Common.Parsing;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Tmdb.Dtos;

namespace SocialMeter.Infrastructure.Tmdb.Mapping;

internal static class TmdbMapper
{
    // TMDB genre ids for list endpoints, which only return ids. Covers both movie and tv lists.
    private static readonly Dictionary<int, string> GenreNames = new()
    {
        [28] = "Action", [12] = "Adventure", [16] = "Animation", [35] = "Comedy", [80] = "Crime",
        [99] = "Documentary", [18] = "Drama", [10751] = "Family", [14] = "Fantasy", [36] = "History",
        [27] = "Horror", [10402] = "Music", [9648] = "Mystery", [10749] = "Romance", [878] = "Sci-Fi",
        [10770] = "TV Movie", [53] = "Thriller", [10752] = "War", [37] = "Western",
        [10759] = "Action & Adventure", [10762] = "Kids", [10763] = "News", [10764] = "Reality",
        [10765] = "Sci-Fi & Fantasy", [10766] = "Soap", [10767] = "Talk", [10768] = "War & Politics"
    };

    /// <summary>Maps an item whose kind is known from the endpoint that returned it.</summary>
    public static Title ToTitle(TmdbTitleItem r, MediaType mediaType)
    {
        var isTv = mediaType == MediaType.Tv;

        var genres = r.Genres is { Count: > 0 }
            ? r.Genres.Select(g => g.Name ?? "").Where(n => n.Length > 0).ToList()
            : (r.GenreIds ?? new List<int>()).Select(id => GenreNames.GetValueOrDefault(id)).OfType<string>().ToList();

        return new Title
        {
            TmdbId = r.Id,
            MediaType = mediaType,
            Name = (isTv ? r.Name : r.Title) ?? r.Name ?? r.Title ?? "",
            Overview = r.Overview ?? "",
            Tagline = string.IsNullOrWhiteSpace(r.Tagline) ? null : r.Tagline,
            PosterPath = r.PosterPath ?? "",
            BackdropPath = r.BackdropPath ?? "",
            ReleaseDate = DateParser.TryParseDateOnly(isTv ? r.FirstAirDate : r.ReleaseDate),
            VoteAverage = r.VoteAverage,
            VoteCount = r.VoteCount,
            Popularity = r.Popularity,
            Genres = genres,
            RuntimeMinutes = isTv ? r.EpisodeRunTime?.FirstOrDefault() : r.Runtime,
            SeasonCount = r.NumberOfSeasons,
            EpisodeCount = r.NumberOfEpisodes,
            Status = r.Status,
            ImdbId = r.ImdbId ?? r.ExternalIds?.ImdbId,
            Homepage = string.IsNullOrWhiteSpace(r.Homepage) ? null : r.Homepage
        };
    }

    /// <summary>Maps a multi-search / trending-all item using its media_type; null for people and unknown kinds.</summary>
    public static Title? ToTitleFromMixed(TmdbTitleItem r) => r.MediaType switch
    {
        "movie" => ToTitle(r, MediaType.Movie),
        "tv" => ToTitle(r, MediaType.Tv),
        _ => null
    };
}
