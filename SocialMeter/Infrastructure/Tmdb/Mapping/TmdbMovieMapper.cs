using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Tmdb.Dtos;
using SocialMeter.Common.Parsing;

namespace SocialMeter.Infrastructure.Tmdb.Mapping;

internal static class TmdbMovieMapper
{
    public static Movie ToNowPlaying(TmdbMovieItem r)
        => new Movie
        {
            TmdbId = r.Id,
            Title = r.Title ?? "",
            PosterPath = r.PosterPath ?? "",
            ReleaseDate = DateParser.TryParseDateOnly(r.ReleaseDate),
            Category = MovieCategory.NowPlaying,
            BackdropPath = r.BackdropPath ?? "",
            Overview = r.Overview ?? "",
            VoteAverage = r.VoteAverage,
        };

    public static Movie ToTrending(TmdbMovieItem r)
        => new Movie
        {
            TmdbId = r.Id,
            Title = r.Title ?? "",
            PosterPath = r.PosterPath ?? "",
            ReleaseDate = DateParser.TryParseDateOnly(r.ReleaseDate),
            Category = MovieCategory.Trending,
            MediaType = MediaType.Movie,
            BackdropPath = r.BackdropPath ?? "",
            Overview = r.Overview ?? "",
            VoteAverage = r.VoteAverage,
        };

    // ADD THIS NEW METHOD
    public static Movie ToSearchResult(TmdbMovieItem r)
        => new Movie
        {
            TmdbId = r.Id,
            Title = r.Title ?? "",
            PosterPath = r.PosterPath ?? "",
            ReleaseDate = DateParser.TryParseDateOnly(r.ReleaseDate),
            Category = MovieCategory.Search, // Different category for search results
            MediaType = MediaType.Movie,
            BackdropPath = r.BackdropPath ?? "",
            Overview = r.Overview ?? "",
            VoteAverage = r.VoteAverage,
        };
}
