using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface ITitleService
{
    /// <summary>Trending for one kind, or movies + shows together when null. For games this is Steam's top sellers.</summary>
    Task<PagedResult<Title>> GetTrendingAsync(MediaType? mediaType, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetNowPlayingAsync(string region, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetOnTheAirAsync(int page = 1, CancellationToken ct = default);

    /// <summary>Popular for one kind. For games this is Steam's most-played chart.</summary>
    Task<PagedResult<Title>> GetPopularAsync(MediaType mediaType, int page = 1, CancellationToken ct = default);

    /// <summary>Top rated for movies/shows; for games, new releases (Steam has no top-rated list).</summary>
    Task<PagedResult<Title>> GetTopRatedAsync(MediaType mediaType, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetUpcomingAsync(string region, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> DiscoverByGenreAsync(MediaType mediaType, int genreId, int page = 1, CancellationToken ct = default);

    /// <summary>Movies in a genre released in the last <paramref name="days"/> days, most popular first.</summary>
    Task<PagedResult<Title>> DiscoverRecentMoviesByGenreAsync(int genreId, int days, int page = 1, CancellationToken ct = default);

    /// <summary>Movies in a genre that opened in theaters in the region within the last <paramref name="days"/> days.</summary>
    Task<PagedResult<Title>> DiscoverInTheatersByGenreAsync(int genreId, string region, int days, int page = 1, CancellationToken ct = default);

    Task<PagedResult<Title>> GetNewGamesAsync(int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetComingSoonGamesAsync(int page = 1, CancellationToken ct = default);

    Task<Title?> GetDetailsAsync(MediaType mediaType, int id, CancellationToken ct = default);

    /// <summary>Search one kind, or everything when null (movies and shows from TMDB with a few Steam matches mixed in).</summary>
    Task<PagedResult<Title>> SearchAsync(string query, MediaType? mediaType, int page = 1, CancellationToken ct = default);
}
