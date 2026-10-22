using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Links;

/// <summary>A score a site publishes about a title, on a 0–100 scale, with how many reviews it rests on.</summary>
public sealed record PublishedScore(int Value, int? Count);

/// <summary>
/// Reads the score Rotten Tomatoes and Metacritic publish in their pages' schema.org structured data (the
/// aggregateRating they provide for search engines), used only when OMDb has none. Only the named score is taken:
/// "Tomatometer" on Rotten Tomatoes, "Metascore" on Metacritic, so an audience score is never shown as a critics' one.
/// The release date beside the score must fall within a year of the title's, so an older film that shares the name
/// never lends its score. Cached per page; switched off entirely by Ratings:ReadPublishedScores.
/// </summary>
public sealed partial class PublishedScoreReader
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly RatingsOptions _options;
    private readonly ILogger<PublishedScoreReader> _logger;

    public PublishedScoreReader(HttpClient http, IMemoryCache cache, IOptions<RatingsOptions> options, ILogger<PublishedScoreReader> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(8);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
    }

    public bool Enabled => _options.ReadPublishedScores;

    /// <summary>The named score on a resolved title page, or null (switched off, a search page, blocked, or no such score).</summary>
    public async Task<PublishedScore?> ReadAsync(string? url, string scoreName, int? year, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(url) || url.Contains("/search", StringComparison.OrdinalIgnoreCase))
            return null;

        var key = $"published:{scoreName}:{year}:{url}";
        if (_cache.TryGetValue(key, out PublishedScore? cached))
            return cached;

        PublishedScore? score = null;
        try
        {
            using var res = await _http.GetAsync(url, ct);
            if (res.IsSuccessStatusCode)
                score = Parse(await res.Content.ReadAsStringAsync(ct), scoreName, year);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Published score read failed for {Url}", url);
            return null; // not cached, so the next view tries again
        }

        _cache.Set(key, score, TimeSpan.FromHours(Math.Max(1, _options.PublishedScoreCacheHours)));
        return score;
    }

    /// <summary>Finds the aggregateRating with the given name in a page's JSON-LD blocks. Public for tests.</summary>
    public static PublishedScore? Parse(string html, string scoreName, int? year = null)
    {
        foreach (Match m in JsonLdRegex().Matches(html))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(m.Groups[1].Value); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var found = Find(doc.RootElement, scoreName, year);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static PublishedScore? Find(JsonElement node, string scoreName, int? year)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                if (Find(item, scoreName, year) is { } hit) return hit;
            return null;
        }
        if (node.ValueKind != JsonValueKind.Object) return null;

        if (node.TryGetProperty("aggregateRating", out var ar) && ar.ValueKind == JsonValueKind.Object
            && ar.TryGetProperty("name", out var name) && string.Equals(name.GetString(), scoreName, StringComparison.OrdinalIgnoreCase)
            && Number(ar, "ratingValue") is { } value
            && (Number(ar, "bestRating") ?? 100) == 100
            && value is >= 0 and <= 100
            && YearMatches(node, year))
        {
            return new PublishedScore((int)Math.Round(value), (int?)(Number(ar, "reviewCount") ?? Number(ar, "ratingCount")));
        }

        foreach (var prop in node.EnumerateObject())
            if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && Find(prop.Value, scoreName, year) is { } hit)
                return hit;
        return null;
    }

    /// <summary>True when the page gives no release date, we know no year, or the two are within a year of each other.</summary>
    private static bool YearMatches(JsonElement node, int? year)
    {
        if (year is null) return true;
        foreach (var prop in new[] { "datePublished", "dateCreated", "startDate" })
        {
            if (node.TryGetProperty(prop, out var d) && d.ValueKind == JsonValueKind.String
                && d.GetString() is { Length: >= 4 } text && int.TryParse(text[..4], out var pageYear))
                return Math.Abs(pageYear - year.Value) <= 1;
        }
        return true;
    }

    private static double? Number(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    [GeneratedRegex(@"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdRegex();
}
