using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Omdb;

/// <summary>Looks a title up on OMDb by IMDb id to get the IMDb, Rotten Tomatoes and Metacritic ratings.</summary>
public sealed class OmdbClient
{
    private readonly HttpClient _http;
    private readonly OmdbOptions _options;

    public OmdbClient(HttpClient http, IOptions<OmdbOptions> options)
    {
        _http = http;
        _options = options.Value;
        var baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";
        _http.BaseAddress = new Uri(baseUrl);
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<OmdbTitle?> GetByImdbIdAsync(string imdbId, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(imdbId))
            return null;

        var url = $"?i={Uri.EscapeDataString(imdbId)}&apikey={Uri.EscapeDataString(_options.ApiKey)}";

        // OMDb answers key problems with a 401 and a JSON body explaining why ("Invalid API key!" for a key whose
        // activation link hasn't been clicked, "Request limit reached!" past the daily quota). Read the body either way.
        using var res = await _http.GetAsync(url, ct);
        var dto = await res.Content.ReadFromJsonAsync<OmdbTitle>(cancellationToken: ct);

        if (dto is { Response: "True" })
            return dto;

        if (dto?.Error is not null)
            throw new InvalidOperationException($"OMDb: {dto.Error}");

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"OMDb request failed ({(int)res.StatusCode} {res.ReasonPhrase}).");

        return null;
    }
}

public sealed class OmdbTitle
{
    public string? Response { get; set; }
    public string? Error { get; set; }
    [JsonPropertyName("imdbRating")] public string? ImdbRating { get; set; }
    [JsonPropertyName("imdbVotes")] public string? ImdbVotes { get; set; }
    public string? Metascore { get; set; }
    public List<OmdbRating>? Ratings { get; set; }
}

public sealed class OmdbRating
{
    public string? Source { get; set; }
    public string? Value { get; set; }
}
