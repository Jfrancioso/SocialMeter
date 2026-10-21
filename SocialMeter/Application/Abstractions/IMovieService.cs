using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface IMovieService
{
    Task<PagedResult<Movie>> GetNowPlayingAsync(string region = "US", int page = 1, CancellationToken ct = default);
    Task<PagedResult<Movie>> GetTrendingAsync(string region = "US", int page = 1, CancellationToken ct = default);
    Task<Movie?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<Movie>> SearchMoviesAsync(string query, int page = 1, CancellationToken ct = default);

}
