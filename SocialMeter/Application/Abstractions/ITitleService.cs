using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface ITitleService
{
    Task<PagedResult<Title>> GetTrendingAsync(MediaType? mediaType, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetNowPlayingAsync(string region, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetOnTheAirAsync(int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetPopularAsync(MediaType mediaType, int page = 1, CancellationToken ct = default);
    Task<PagedResult<Title>> GetTopRatedAsync(MediaType mediaType, int page = 1, CancellationToken ct = default);
    Task<Title?> GetDetailsAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default);
    Task<PagedResult<Title>> SearchAsync(string query, MediaType? mediaType, int page = 1, CancellationToken ct = default);
}
