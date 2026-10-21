using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Domain.Models;

namespace SocialMeter.Infrastructure.Links;

/// <summary>
/// Rotten Tomatoes and Metacritic publish no id we can map a title to, but their page URLs follow a predictable
/// slug pattern. This guesses the slug (plus a year-suffixed variant for remakes), confirms the page exists with a
/// lightweight request, caches the answer, and falls back to the site's search when nothing matches.
/// </summary>
public sealed partial class RatingLinkResolver
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromDays(7);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RatingLinkResolver> _logger;

    public RatingLinkResolver(HttpClient http, IMemoryCache cache, ILogger<RatingLinkResolver> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(6);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SocialMeter/1.0 (+link verification only)");
    }

    public Task<string> RottenTomatoesAsync(Title title, CancellationToken ct)
    {
        var slug = Slug(title.Name, '_');
        var section = title.MediaType == MediaType.Tv ? "tv" : "m";

        // The year-suffixed slug only exists when titles collide (remakes), and then it is the page for this film,
        // while the bare slug belongs to the older one — so try the suffixed form first.
        var candidates = new List<string>();
        if (title.Year is { } y) candidates.Add($"https://www.rottentomatoes.com/{section}/{slug}_{y}");
        candidates.Add($"https://www.rottentomatoes.com/{section}/{slug}");

        var query = title.Year is { } yr ? $"{title.Name} {yr}" : title.Name;
        var fallback = $"https://www.rottentomatoes.com/search?search={Uri.EscapeDataString(query)}";
        return ResolveAsync($"rt:{title.MediaType}:{title.TmdbId}", candidates, fallback, ct);
    }

    public Task<string> MetacriticAsync(Title title, CancellationToken ct)
    {
        var slug = Slug(title.Name, '-');
        var section = title.MediaType == MediaType.Tv ? "tv" : "movie";
        var candidates = new List<string>();
        if (title.Year is { } y) candidates.Add($"https://www.metacritic.com/{section}/{slug}-{y}/");
        candidates.Add($"https://www.metacritic.com/{section}/{slug}/");

        var fallback = $"https://www.metacritic.com/search/{Uri.EscapeDataString(title.Name)}/";
        return ResolveAsync($"mc:{title.MediaType}:{title.TmdbId}", candidates, fallback, ct);
    }

    private async Task<string> ResolveAsync(string cacheKey, List<string> candidates, string fallback, CancellationToken ct)
    {
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached;

        foreach (var url in candidates)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.IsSuccessStatusCode)
                {
                    _cache.Set(cacheKey, url, CacheFor);
                    return url;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Blocked or slow: don't hold the ratings up, and don't cache so a later request can try again.
                _logger.LogDebug(ex, "Link check failed for {Url}", url);
                return fallback;
            }
        }

        _cache.Set(cacheKey, fallback, CacheFor);
        return fallback;
    }

    /// <summary>"Dune: Part Two" → "dune_part_two" (or "dune-part-two"). Accents are stripped so "Amélie" → "amelie".</summary>
    internal static string Slug(string name, char separator)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            ascii.Append(c == '&' ? " and " : c.ToString());
        }

        var lowered = ascii.ToString().ToLowerInvariant();
        var collapsed = NonAlphanumeric().Replace(lowered, separator.ToString());
        return collapsed.Trim(separator);
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
