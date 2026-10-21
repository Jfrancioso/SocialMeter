using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Tmdb;

namespace SocialMeter.Application.Movies;

public sealed class MovieService : IMovieService
{
    private readonly TmdbClient _tmdb;

    public MovieService(TmdbClient tmdb)
    {
        _tmdb = tmdb;
    }

    public Task<PagedResult<Movie>> GetNowPlayingAsync(string region = "US", int page = 1, CancellationToken ct = default)
        => _tmdb.GetNowPlayingAsync(region, page, ct);

    public Task<PagedResult<Movie>> GetTrendingAsync(string region = "US", int page = 1, CancellationToken ct = default)
        => _tmdb.GetTrendingAsync(page, ct);

    public Task<Movie?> GetByIdAsync(int id, CancellationToken ct = default)
        => _tmdb.GetByIdAsync(id, ct);
    public Task<PagedResult<Movie>> SearchMoviesAsync(string query, int page = 1, CancellationToken ct = default)
     => _tmdb.SearchMoviesAsync(query, page, ct);
}
