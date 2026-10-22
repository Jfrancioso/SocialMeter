using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Steam;
using SocialMeter.Infrastructure.Tmdb;

namespace SocialMeter.Application.Titles;

/// <summary>Routes each request to the provider for its kind: TMDB for movies and shows, Steam for games.</summary>
public sealed class TitleService : ITitleService
{
    private static readonly TimeSpan DetailsCacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan MissCacheFor = TimeSpan.FromMinutes(10);

    private readonly TmdbClient _tmdb;
    private readonly SteamClient _steam;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TitleService> _logger;

    public TitleService(TmdbClient tmdb, SteamClient steam, IMemoryCache cache, ILogger<TitleService> logger)
    {
        _tmdb = tmdb;
        _steam = steam;
        _cache = cache;
        _logger = logger;
    }

    public Task<PagedResult<Title>> GetTrendingAsync(MediaType? mediaType, int page = 1, CancellationToken ct = default)
        => mediaType == MediaType.Game ? _steam.GetTopSellersAsync(page, ct) : _tmdb.GetTrendingAsync(mediaType, page, ct);

    public Task<PagedResult<Title>> GetNowPlayingAsync(string region, int page = 1, CancellationToken ct = default)
        => _tmdb.GetNowPlayingAsync(region, page, ct);

    public Task<PagedResult<Title>> GetOnTheAirAsync(int page = 1, CancellationToken ct = default)
        => _tmdb.GetOnTheAirAsync(page, ct);

    public Task<PagedResult<Title>> GetPopularAsync(MediaType mediaType, int page = 1, CancellationToken ct = default)
        => mediaType == MediaType.Game ? _steam.GetMostPlayedAsync(page, ct) : _tmdb.GetPopularAsync(mediaType, page, ct);

    public Task<PagedResult<Title>> GetTopRatedAsync(MediaType mediaType, int page = 1, CancellationToken ct = default)
        => mediaType == MediaType.Game ? _steam.GetNewReleasesAsync(page, ct) : _tmdb.GetTopRatedAsync(mediaType, page, ct);

    public Task<PagedResult<Title>> GetUpcomingAsync(string region, int page = 1, CancellationToken ct = default)
        => _tmdb.GetUpcomingAsync(region, page, ct);

    public Task<PagedResult<Title>> DiscoverByGenreAsync(MediaType mediaType, int genreId, int page = 1, CancellationToken ct = default)
        => mediaType == MediaType.Game
            ? Task.FromResult(new PagedResult<Title>(Array.Empty<Title>(), page, 0, 0))
            : _tmdb.DiscoverByGenreAsync(mediaType, genreId, page, ct);

    public Task<PagedResult<Title>> DiscoverRecentMoviesByGenreAsync(int genreId, int days, int page = 1, CancellationToken ct = default)
        => _tmdb.DiscoverRecentMoviesByGenreAsync(genreId, days, page, ct);

    public Task<PagedResult<Title>> DiscoverInTheatersByGenreAsync(int genreId, string region, int days, int page = 1, CancellationToken ct = default)
        => _tmdb.DiscoverInTheatersByGenreAsync(genreId, region, days, page, ct);

    public Task<PagedResult<Title>> GetNewGamesAsync(int page = 1, CancellationToken ct = default)
        => _steam.GetNewReleasesAsync(page, ct);

    public Task<PagedResult<Title>> GetComingSoonGamesAsync(int page = 1, CancellationToken ct = default)
        => _steam.GetComingSoonAsync(page, ct);

    /// <summary>
    /// Title details are requested by every page view and by every scoring pass, and they change slowly. Caching them
    /// also means walking the id space costs the provider quota once per title per six hours, not once per request.
    /// </summary>
    public async Task<Title?> GetDetailsAsync(MediaType mediaType, int id, CancellationToken ct = default)
    {
        var key = $"title:{mediaType}:{id}";
        if (_cache.TryGetValue(key, out Title? cached))
            return cached;

        var title = mediaType == MediaType.Game ? await _steam.GetDetailsAsync(id, ct) : await _tmdb.GetDetailsAsync(mediaType, id, ct);
        _cache.Set(key, title, title is null ? MissCacheFor : DetailsCacheFor);
        return title;
    }

    public async Task<PagedResult<Title>> SearchAsync(string query, MediaType? mediaType, int page = 1, CancellationToken ct = default)
    {
        if (mediaType == MediaType.Game)
            return await _steam.SearchAsync(query, page, ct);

        var tmdb = await _tmdb.SearchAsync(query, mediaType, page, ct);
        if (mediaType is not null || page != 1)
            return tmdb;

        // "Everything" search: a handful of Steam matches ride along on the first page so games are discoverable.
        try
        {
            var games = await _steam.SearchAsync(query, 1, ct);
            // Slot the games in after the first couple of TMDB hits so they're visible without burying the obvious match.
            var mixed = tmdb.Items.ToList();
            mixed.InsertRange(Math.Min(2, mixed.Count), games.Items.Take(4));
            return new PagedResult<Title>(mixed, tmdb.Page, tmdb.TotalPages, tmdb.TotalResults + games.TotalResults);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Steam search failed for {Query}; returning TMDB results only", query);
            return tmdb;
        }
    }
}
