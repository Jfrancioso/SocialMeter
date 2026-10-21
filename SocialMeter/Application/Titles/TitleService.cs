using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Tmdb;

namespace SocialMeter.Application.Titles;

public sealed class TitleService : ITitleService
{
    private readonly TmdbClient _tmdb;

    public TitleService(TmdbClient tmdb)
    {
        _tmdb = tmdb;
    }

    public Task<PagedResult<Title>> GetTrendingAsync(MediaType? mediaType, int page = 1, CancellationToken ct = default)
        => _tmdb.GetTrendingAsync(mediaType, page, ct);

    public Task<PagedResult<Title>> GetNowPlayingAsync(string region, int page = 1, CancellationToken ct = default)
        => _tmdb.GetNowPlayingAsync(region, page, ct);

    public Task<PagedResult<Title>> GetOnTheAirAsync(int page = 1, CancellationToken ct = default)
        => _tmdb.GetOnTheAirAsync(page, ct);

    public Task<PagedResult<Title>> GetPopularAsync(MediaType mediaType, int page = 1, CancellationToken ct = default)
        => _tmdb.GetPopularAsync(mediaType, page, ct);

    public Task<PagedResult<Title>> GetTopRatedAsync(MediaType mediaType, int page = 1, CancellationToken ct = default)
        => _tmdb.GetTopRatedAsync(mediaType, page, ct);

    public Task<PagedResult<Title>> GetUpcomingAsync(string region, int page = 1, CancellationToken ct = default)
        => _tmdb.GetUpcomingAsync(region, page, ct);

    public Task<PagedResult<Title>> DiscoverByGenreAsync(MediaType mediaType, int genreId, int page = 1, CancellationToken ct = default)
        => _tmdb.DiscoverByGenreAsync(mediaType, genreId, page, ct);

    public Task<Title?> GetDetailsAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default)
        => _tmdb.GetDetailsAsync(mediaType, tmdbId, ct);

    public Task<PagedResult<Title>> SearchAsync(string query, MediaType? mediaType, int page = 1, CancellationToken ct = default)
        => _tmdb.SearchAsync(query, mediaType, page, ct);
}
