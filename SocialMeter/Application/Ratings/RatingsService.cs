using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Omdb;

namespace SocialMeter.Application.Ratings;

public sealed class RatingsService : IRatingsService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

    private readonly OmdbClient _omdb;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RatingsService> _logger;

    public RatingsService(OmdbClient omdb, IMemoryCache cache, ILogger<RatingsService> logger)
    {
        _omdb = omdb;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExternalRating>> GetRatingsAsync(Title title, CancellationToken ct = default)
    {
        var key = $"ratings:{title.MediaType}:{title.TmdbId}";
        if (_cache.TryGetValue(key, out IReadOnlyList<ExternalRating>? cached) && cached is not null)
            return cached;

        var ratings = new List<ExternalRating>
        {
            new()
            {
                Source = "tmdb",
                DisplayName = "TMDB",
                Value = title.VoteCount > 0 ? $"{title.VoteAverage:0.0}/10" : null,
                Normalized = title.VoteCount > 0 ? (int)Math.Round(title.VoteAverage * 10) : null,
                Detail = title.VoteCount > 0 ? $"{title.VoteCount:N0} votes" : "No votes yet",
                Url = $"https://www.themoviedb.org/{(title.MediaType == MediaType.Tv ? "tv" : "movie")}/{title.TmdbId}"
            }
        };

        OmdbTitle? omdb = null;
        if (_omdb.IsConfigured && !string.IsNullOrWhiteSpace(title.ImdbId))
        {
            try
            {
                omdb = await _omdb.GetByImdbIdAsync(title.ImdbId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OMDb lookup failed for {ImdbId}", title.ImdbId);
            }
        }

        var imdbUrl = title.ImdbId is null ? null : $"https://www.imdb.com/title/{title.ImdbId}/";
        var rt = omdb?.Ratings?.FirstOrDefault(r => r.Source == "Rotten Tomatoes")?.Value;

        ratings.Add(Build("imdb", "IMDb", omdb?.ImdbRating, ParseOutOfTen(omdb?.ImdbRating),
            omdb?.ImdbVotes is { } v && v != "N/A" ? $"{v} votes" : null, imdbUrl, "/10"));
        ratings.Add(Build("rottenTomatoes", "Rotten Tomatoes", rt, ParsePercent(rt), "Tomatometer", null, ""));
        ratings.Add(Build("metacritic", "Metacritic", omdb?.Metascore, ParseInt(omdb?.Metascore), "Metascore", null, "/100"));

        if (!_omdb.IsConfigured)
            _logger.LogDebug("OMDb not configured; IMDb/RT/Metacritic ratings unavailable.");

        _cache.Set(key, (IReadOnlyList<ExternalRating>)ratings, CacheFor);
        return ratings;
    }

    private static ExternalRating Build(string source, string name, string? raw, int? normalized, string? detail, string? url, string suffix)
    {
        var available = normalized is not null && raw is not null && raw != "N/A";
        return new ExternalRating
        {
            Source = source,
            DisplayName = name,
            Value = available ? (raw!.EndsWith('%') || suffix.Length == 0 ? raw : raw + suffix) : null,
            Normalized = available ? normalized : null,
            Detail = available ? detail : "Not available",
            Url = url
        };
    }

    private static int? ParseOutOfTen(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d * 10) : null;

    private static int? ParsePercent(string? s)
        => s is not null && s.EndsWith('%') && int.TryParse(s[..^1], out var i) ? i : null;

    private static int? ParseInt(string? s)
        => int.TryParse(s, out var i) ? i : null;
}
