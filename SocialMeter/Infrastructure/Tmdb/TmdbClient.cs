using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
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

    public Task<PagedResult<Title>> GetTrendingAsync(MediaType? mediaType, int page, CancellationToken ct)
    {
        var segment = mediaType switch
        {
            MediaType.Movie => "movie",
            MediaType.Tv => "tv",
            _ => "all"
        };
        return mediaType is null
            ? GetMixedListAsync($"trending/all/day", page, ct)
            : GetListAsync($"trending/{segment}/day", mediaType.Value, page, ct);
    }

    public Task<PagedResult<Title>> GetNowPlayingAsync(string region, int page, CancellationToken ct)
        => GetListAsync($"movie/now_playing?region={Uri.EscapeDataString(NormalizeRegion(region))}", MediaType.Movie, page, ct);

    public Task<PagedResult<Title>> GetOnTheAirAsync(int page, CancellationToken ct)
        => GetListAsync("tv/on_the_air", MediaType.Tv, page, ct);

    public Task<PagedResult<Title>> GetPopularAsync(MediaType mediaType, int page, CancellationToken ct)
        => GetListAsync($"{Segment(mediaType)}/popular", mediaType, page, ct);

    public Task<PagedResult<Title>> GetTopRatedAsync(MediaType mediaType, int page, CancellationToken ct)
        => GetListAsync($"{Segment(mediaType)}/top_rated", mediaType, page, ct);

    public Task<PagedResult<Title>> GetUpcomingAsync(string region, int page, CancellationToken ct)
        => GetListAsync($"movie/upcoming?region={Uri.EscapeDataString(NormalizeRegion(region))}", MediaType.Movie, page, ct);

    /// <summary>Most popular titles in a genre right now — how the seasonal theme finds its picks.</summary>
    public Task<PagedResult<Title>> DiscoverByGenreAsync(MediaType mediaType, int genreId, int page, CancellationToken ct)
        => GetListAsync($"discover/{Segment(mediaType)}?with_genres={genreId}&sort_by=popularity.desc&include_adult=false&vote_count.gte=50", mediaType, page, ct);

    public Task<PagedResult<Title>> DiscoverTvByKeywordAsync(int keywordId, int page, CancellationToken ct)
        => GetListAsync($"discover/tv?with_keywords={keywordId}&sort_by=popularity.desc&include_adult=false&vote_count.gte=50", MediaType.Tv, page, ct);

    /// <summary>Movies in a genre released in the last <paramref name="days"/> days, most popular first: "what's hot in horror right now".</summary>
    public Task<PagedResult<Title>> DiscoverRecentMoviesByGenreAsync(int genreId, int days, int page, CancellationToken ct)
    {
        var from = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return GetListAsync($"discover/movie?with_genres={genreId}&primary_release_date.gte={from}&primary_release_date.lte={to}&sort_by=popularity.desc&include_adult=false&vote_count.gte=10", MediaType.Movie, page, ct);
    }

    /// <summary>Movies in a genre with a theatrical release in the region in the last <paramref name="days"/> days: "new in theaters".</summary>
    public Task<PagedResult<Title>> DiscoverInTheatersByGenreAsync(int genreId, string region, int days, int page, CancellationToken ct)
    {
        var from = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var r = Uri.EscapeDataString(NormalizeRegion(region));
        // Release types 2 and 3 are limited and wide theatrical; the pipe means "either". The first release must be within
        // the last year too, or anniversary re-releases (Friday the 13th, 1980) count as "new in theaters"; a year still
        // allows a festival premiere followed by a theatrical run.
        var firstReleasedFrom = DateTime.UtcNow.AddDays(-365).ToString("yyyy-MM-dd");
        return GetListAsync($"discover/movie?with_genres={genreId}&region={r}&with_release_type=2|3&release_date.gte={from}&release_date.lte={to}&primary_release_date.gte={firstReleasedFrom}&sort_by=popularity.desc&include_adult=false", MediaType.Movie, page, ct);
    }

    public Task<PagedResult<Title>> SearchAsync(string query, MediaType? mediaType, int page, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(Empty(page));

        var q = $"query={Uri.EscapeDataString(query.Trim())}&include_adult=false";
        return mediaType is null
            ? GetMixedListAsync($"search/multi?{q}", page, ct)
            : GetListAsync($"search/{Segment(mediaType.Value)}?{q}", mediaType.Value, page, ct);
    }

    public async Task<Title?> GetDetailsAsync(MediaType mediaType, int id, CancellationToken ct)
    {
        EnsureConfigured();
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "id must be > 0");

        // Videos and credits ride along on the same request: trailers and cast cost no extra call. English plus
        // language-neutral videos. Cast and creators help search for titles whose names are everyday words (TitleSearch).
        var url = WithKey($"{Segment(mediaType)}/{id}?append_to_response=external_ids,videos,credits&include_video_language=en,null");

        using var res = await _http.GetAsync(url, ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"TMDB details failed ({(int)res.StatusCode}): {body}");
        }

        var dto = await res.Content.ReadFromJsonAsync<TmdbTitleItem>(cancellationToken: ct);
        return dto is null ? null : TmdbMapper.ToTitle(dto, mediaType);
    }

    private async Task<PagedResult<Title>> GetListAsync(string pathAndQuery, MediaType mediaType, int page, CancellationToken ct)
    {
        EnsureConfigured();
        page = ClampPage(page);

        var dto = await _http.GetFromJsonAsync<TmdbPagedResponse<TmdbTitleItem>>(WithKey(pathAndQuery, page), ct);
        if (dto is null) return Empty(page);

        var items = dto.Results.Select(r => TmdbMapper.ToTitle(r, mediaType)).ToList();
        return new PagedResult<Title>(items, dto.Page == 0 ? page : dto.Page, dto.TotalPages, dto.TotalResults);
    }

    private async Task<PagedResult<Title>> GetMixedListAsync(string pathAndQuery, int page, CancellationToken ct)
    {
        EnsureConfigured();
        page = ClampPage(page);

        var dto = await _http.GetFromJsonAsync<TmdbPagedResponse<TmdbTitleItem>>(WithKey(pathAndQuery, page), ct);
        if (dto is null) return Empty(page);

        var items = dto.Results.Select(TmdbMapper.ToTitleFromMixed).OfType<Title>().ToList();
        return new PagedResult<Title>(items, dto.Page == 0 ? page : dto.Page, dto.TotalPages, dto.TotalResults);
    }

    // With a read-access token the credential rides in the Authorization header (set on the HttpClient at registration),
    // so nothing secret is ever part of the URL. The v3 key in the query string is the fallback.
    private string WithKey(string pathAndQuery, int? page = null)
    {
        var url = pathAndQuery;
        if (!_options.UseBearer)
        {
            var sep = pathAndQuery.Contains('?') ? "&" : "?";
            url = $"{pathAndQuery}{sep}api_key={Uri.EscapeDataString(_options.ApiKey)}";
        }
        if (page is null) return url;
        return $"{url}{(url.Contains('?') ? "&" : "?")}page={page}";
    }

    private static string Segment(MediaType mediaType) => mediaType == MediaType.Tv ? "tv" : "movie";

    private static PagedResult<Title> Empty(int page) => new(Array.Empty<Title>(), page, 0, 0);

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("TMDB is not configured: set Tmdb:ReadAccessToken or Tmdb:ApiKey.");

        if (_http.BaseAddress is null)
            throw new InvalidOperationException("TMDB HttpClient BaseAddress is not configured.");
    }

    private static int ClampPage(int page) => page < 1 ? 1 : page;

    private string NormalizeRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            region = _options.Region;

        region = region.Trim().ToUpperInvariant();
        return region.Length == 2 ? region : _options.Region;
    }
}
