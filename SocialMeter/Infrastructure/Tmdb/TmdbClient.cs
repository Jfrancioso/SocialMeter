using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SocialMeter.Common.Parsing;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Tmdb.Dtos;
using SocialMeter.Infrastructure.Tmdb.Mapping;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Tmdb;

public sealed class TmdbClient
{
    private readonly HttpClient _http;
    private readonly TmdbOptions _options;

    public TmdbClient(HttpClient http, IOptions<TmdbOptions> options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PagedResult<Movie>> GetNowPlayingAsync(string region = "US", int page = 1, CancellationToken ct = default)
    {
        EnsureConfigured();

        page = ClampPage(page);
        region = NormalizeRegion(region);

        var url =
            $"movie/now_playing" +
            $"?api_key={Uri.EscapeDataString(_options.ApiKey)}" +
            $"&region={Uri.EscapeDataString(region)}" +
            $"&page={page}";

        var dto = await _http.GetFromJsonAsync<TmdbPagedResponse<TmdbMovieItem>>(url, ct);

        // dto can be null if deserialization fails or body is empty
        if (dto is null)
        {
            return new PagedResult<Movie>(Array.Empty<Movie>(), page, 0, 0);
        }

        var items = dto.Results.Select(TmdbMovieMapper.ToNowPlaying).ToList();

        return new PagedResult<Movie>(
            items,
            dto.Page == 0 ? page : dto.Page,
            dto.TotalPages,
            dto.TotalResults
        );
    }

    public async Task<PagedResult<Movie>> GetTrendingAsync(int page = 1, CancellationToken ct = default)
    {
        EnsureConfigured();

        page = ClampPage(page);

        var url =
            $"trending/movie/day" +
            $"?api_key={Uri.EscapeDataString(_options.ApiKey)}" +
            $"&page={page}";

        var dto = await _http.GetFromJsonAsync<TmdbPagedResponse<TmdbMovieItem>>(url, ct);

        if (dto is null)
        {
            return new PagedResult<Movie>(Array.Empty<Movie>(), page, 0, 0);
        }

        var items = dto.Results.Select(TmdbMovieMapper.ToTrending).ToList();

        return new PagedResult<Movie>(
            items,
            dto.Page == 0 ? page : dto.Page,
            dto.TotalPages,
            dto.TotalResults
        );
    }

    public async Task<Movie?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        EnsureConfigured();

        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "id must be > 0");

        var url = $"movie/{id}?api_key={Uri.EscapeDataString(_options.ApiKey)}";

        using var res = await _http.GetAsync(url, ct);

        if (res.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!res.IsSuccessStatusCode)
        {
            // read body so you can see TMDB message in logs/debug
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"TMDB GetById failed ({(int)res.StatusCode}): {body}");
        }

        var dto = await res.Content.ReadFromJsonAsync<TmdbMovieItem>(cancellationToken: ct);

        if (dto is null) return null;

        return new Movie
        {
            TmdbId = dto.Id,
            Title = dto.Title ?? "",
            PosterPath = dto.PosterPath ?? "",
            BackdropPath = dto.BackdropPath ?? "",
            Overview = dto.Overview ?? "",
            ReleaseDate = SocialMeter.Common.Parsing.DateParser.TryParseDateOnly(dto.ReleaseDate),
            VoteAverage = dto.VoteAverage,
            MediaType = MediaType.Movie
        };
    }

    public async Task<PagedResult<Movie>> SearchMoviesAsync(string query, int page = 1, CancellationToken ct = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(query))
        {
            return new PagedResult<Movie>(Array.Empty<Movie>(), page, 0, 0);
        }

        page = ClampPage(page);

        var url =
            $"search/movie" +
            $"?api_key={Uri.EscapeDataString(_options.ApiKey)}" +
            $"&query={Uri.EscapeDataString(query)}" +
            $"&page={page}";

        var dto = await _http.GetFromJsonAsync<TmdbPagedResponse<TmdbMovieItem>>(url, ct);

        if (dto is null)
        {
            return new PagedResult<Movie>(Array.Empty<Movie>(), page, 0, 0);
        }

        var items = dto.Results.Select(TmdbMovieMapper.ToSearchResult).ToList();

        return new PagedResult<Movie>(
            items,
            dto.Page == 0 ? page : dto.Page,
            dto.TotalPages,
            dto.TotalResults
        );
    }
    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("TMDB is not configured: Tmdb:ApiKey is missing.");

        // BaseAddress is set in DI, but this makes failures obvious if DI is wrong
        if (_http.BaseAddress is null)
            throw new InvalidOperationException("TMDB HttpClient BaseAddress is not configured.");
    }

    private static int ClampPage(int page)
        => page < 1 ? 1 : page;

    private string NormalizeRegion(string region)
    {
        // fallback to configured default
        if (string.IsNullOrWhiteSpace(region))
            region = _options.Region;

        region = region.Trim().ToUpperInvariant();

        // TMDB expects 2-letter region codes; if not, fall back
        if (region.Length != 2)
            region = _options.Region;

        return region;
    }
}
