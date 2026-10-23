using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Trailers;

namespace SocialMeter.Infrastructure.Steam;

/// <summary>
/// Games come from Steam's public store and charts endpoints — no key needed. Lists (top sellers, new releases,
/// coming soon, most played) say what is trending; appdetails fills in a title; appreviews supplies Steam's own
/// review score for the ratings strip. Details are cached for a day since the store rate-limits bursts.
/// </summary>
public sealed class SteamClient
{
    private const string StoreBase = "https://store.steampowered.com/";
    private const string ApiBase = "https://api.steampowered.com/";
    private const string StoreAssetRoot = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private const string AssetBase = StoreAssetRoot + "steam/apps/";

    private static readonly TimeSpan DetailsCache = TimeSpan.FromHours(24);
    private static readonly TimeSpan ListCache = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SteamClient> _logger;

    public SteamClient(HttpClient http, IMemoryCache cache, ILogger<SteamClient> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SocialMeter/1.0 (+audience sentiment meter)");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    public static string PosterUrl(int appId) => $"{AssetBase}{appId}/library_600x900.jpg";
    public static string BackdropUrl(int appId) => $"{AssetBase}{appId}/library_hero.jpg";

    // ---- lists ----

    public Task<PagedResult<Title>> GetTopSellersAsync(int page, CancellationToken ct) => FeaturedListAsync("top_sellers", page, ct);
    public Task<PagedResult<Title>> GetNewReleasesAsync(int page, CancellationToken ct) => FeaturedListAsync("new_releases", page, ct);
    public Task<PagedResult<Title>> GetComingSoonAsync(int page, CancellationToken ct) => FeaturedListAsync("coming_soon", page, ct);

    /// <summary>Steam's most-played chart. The chart only carries app ids, so each one is resolved through (cached) details.</summary>
    public async Task<PagedResult<Title>> GetMostPlayedAsync(int page, CancellationToken ct)
    {
        const int pageSize = 20;
        var ranks = await _cache.GetOrCreateAsync("steam:mostplayed", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var dto = await _http.GetFromJsonAsync<MostPlayedResponse>(ApiBase + "ISteamChartsService/GetMostPlayedGames/v1/", ct);
            return dto?.Response?.Ranks?.Select(r => r.AppId).ToList() ?? new List<int>();
        }) ?? new List<int>();

        var slice = ranks.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var items = new List<Title>(slice.Count);
        foreach (var appId in slice)
        {
            var t = await GetDetailsAsync(appId, ct);
            if (t is not null) items.Add(t);
        }

        return new PagedResult<Title>(items, page, (int)Math.Ceiling(ranks.Count / (double)pageSize), ranks.Count);
    }

    private async Task<PagedResult<Title>> FeaturedListAsync(string category, int page, CancellationToken ct)
    {
        const int pageSize = 20;
        var all = await _cache.GetOrCreateAsync($"steam:featured:{category}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = ListCache;
            var json = await _http.GetStringAsync(StoreBase + "api/featuredcategories?cc=US&l=en", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(category, out var cat) || !cat.TryGetProperty("items", out var items))
                return new List<FeaturedItem>();

            return items.EnumerateArray()
                .Where(i => i.TryGetProperty("type", out var type) && type.GetInt32() == 0) // 0 = game app
                .Select(i => new FeaturedItem(i.GetProperty("id").GetInt32(), i.GetProperty("name").GetString() ?? ""))
                .DistinctBy(i => i.Id) // the store repeats an item when it sits in several sub-sections
                .ToList();
        }) ?? new List<FeaturedItem>();

        // Lists carry only id + name; full details are what cards and the tracker need.
        var slice = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var titles = new List<Title>(slice.Count);
        foreach (var item in slice)
        {
            var t = await GetDetailsAsync(item.Id, ct);
            if (t is not null) titles.Add(t);
        }

        return new PagedResult<Title>(titles, page, (int)Math.Ceiling(all.Count / (double)pageSize), all.Count);
    }

    // ---- search ----

    public async Task<PagedResult<Title>> SearchAsync(string query, int page, CancellationToken ct)
    {
        const int pageSize = 20;
        var url = StoreBase + $"api/storesearch/?term={Uri.EscapeDataString(query)}&cc=US&l=en";
        var dto = await _http.GetFromJsonAsync<StoreSearchResponse>(url, ct);
        var hits = (dto?.Items ?? new List<StoreSearchItem>()).Where(i => i.Type == "app").ToList();

        var slice = hits.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var titles = new List<Title>(slice.Count);
        foreach (var hit in slice)
        {
            var t = await GetDetailsAsync(hit.Id, ct);
            if (t is not null) titles.Add(t);
        }

        return new PagedResult<Title>(titles, page, (int)Math.Ceiling(hits.Count / (double)pageSize), hits.Count);
    }

    // ---- details ----

    public async Task<Title?> GetDetailsAsync(int appId, CancellationToken ct)
    {
        if (appId <= 0) return null;

        return await _cache.GetOrCreateAsync($"steam:app:{appId}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = DetailsCache;
            try
            {
                var json = await _http.GetStringAsync(StoreBase + $"api/appdetails?appids={appId}&cc=US&l=en", ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(appId.ToString(), out var entry)
                    || !entry.TryGetProperty("success", out var ok) || !ok.GetBoolean()
                    || !entry.TryGetProperty("data", out var data))
                    return null;

                if (data.GetProperty("type").GetString() != "game")
                    return null;

                var reviews = await GetReviewSummaryAsync(appId, ct);
                var art = await GetLibraryArtAsync(appId, ct);
                return Map(appId, data, reviews, art);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Steam appdetails failed for {AppId}", appId);
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return null;
            }
        });
    }

    /// <summary>Steam's own all-time review summary. (The endpoint ignores day_range for the summary, so no "recent" variant.)</summary>
    private async Task<ReviewSummary?> GetReviewSummaryAsync(int appId, CancellationToken ct)
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<ReviewsResponse>(StoreBase + $"appreviews/{appId}?json=1&language=all&purchase_type=all&num_per_page=0", ct);
            return dto?.QuerySummary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Steam reviews unavailable for {AppId}", appId);
            return null;
        }
    }

    /// <summary>
    /// IStoreBrowseService/GetItems (public, no key) returns the hashed filenames of a game's real library artwork —
    /// the 600×900 capsule and the wide hero — for new and old games alike. appdetails never exposes these.
    /// Returns null when the call fails; callers fall back to appdetails images.
    /// </summary>
    private async Task<LibraryArt?> GetLibraryArtAsync(int appId, CancellationToken ct)
    {
        try
        {
            var input = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"english\",\"country_code\":\"US\"}},\"data_request\":{{\"include_assets\":true}}}}";
            var json = await _http.GetStringAsync(ApiBase + $"IStoreBrowseService/GetItems/v1/?input_json={Uri.EscapeDataString(input)}", ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("store_items", out var items)
                || items.GetArrayLength() == 0
                || !items[0].TryGetProperty("assets", out var assets))
                return null;

            // e.g. "steam/apps/4435490/${FILENAME}?t=1792523954" — each asset carries its own hashed sub-path.
            var format = assets.TryGetProperty("asset_url_format", out var f) ? f.GetString() : null;
            if (string.IsNullOrEmpty(format)) return null;

            string? Resolve(string key) =>
                assets.TryGetProperty(key, out var file) && file.ValueKind == JsonValueKind.String
                    ? StoreAssetRoot + format.Replace("${FILENAME}", file.GetString())
                    : null;

            return new LibraryArt(Resolve("library_capsule_2x") ?? Resolve("library_capsule"), Resolve("library_hero_2x") ?? Resolve("library_hero"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Steam GetItems (library art) failed for {AppId}", appId);
            return null;
        }
    }

    private sealed record LibraryArt(string? Capsule, string? Hero);

    private static Title Map(int appId, JsonElement d, ReviewSummary? reviews, LibraryArt? art)
    {
        string? Str(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        List<string> StrList(string name, string? field = null) =>
            d.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(x => field is null ? x.GetString() : x.GetProperty(field).GetString()).OfType<string>().ToList()
                : new List<string>();

        DateOnly? release = null;
        if (d.TryGetProperty("release_date", out var rd) && rd.TryGetProperty("date", out var dateEl))
            release = ParseSteamDate(dateEl.GetString());

        int? metaScore = null;
        string? metaUrl = null;
        if (d.TryGetProperty("metacritic", out var mc))
        {
            if (mc.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number) metaScore = s.GetInt32();
            if (mc.TryGetProperty("url", out var u)) metaUrl = u.GetString();
        }

        var platforms = new List<string>();
        if (d.TryGetProperty("platforms", out var p))
            foreach (var prop in p.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.True)
                    platforms.Add(prop.Name switch { "windows" => "Windows", "mac" => "Mac", "linux" => "Linux", _ => prop.Name });

        int? pct = null;
        if (reviews is { TotalReviews: > 0 })
            pct = (int)Math.Round(100.0 * reviews.TotalPositive / reviews.TotalReviews);

        var developers = StrList("developers");
        return new Title
        {
            TmdbId = appId,
            MediaType = MediaType.Game,
            Name = CleanName(Str("name") ?? ""),
            Overview = Str("short_description") ?? "",
            // Portrait art: GetItems knows the real capsule for every game; the legacy un-hashed path is the
            // last resort (works for older games), and the UI falls back to the header if even that 404s.
            PosterPath = art?.Capsule ?? PosterUrl(appId),
            // Hero: the library hero when known, else the store page background (big, real, hashed URL),
            // else the first screenshot, else the header. All of these come straight from Steam, nothing guessed.
            BackdropPath = art?.Hero ?? Str("background_raw") ?? FirstScreenshot(d) ?? Str("header_image") ?? BackdropUrl(appId),
            // Cards need something that always exists when the portrait 404s: the header.
            HeaderImage = Str("header_image"),
            ReleaseDate = release,
            Genres = StrList("genres", "description"),
            Developer = developers.Count > 0 ? string.Join(", ", developers) : null,
            Platforms = platforms,
            Status = d.TryGetProperty("release_date", out var rd2) && rd2.TryGetProperty("coming_soon", out var cs) && cs.GetBoolean() ? "Coming soon" : null,
            Homepage = Str("website"),
            Trailer = TrailerPicker.FromSteam(d),
            MetacriticScore = metaScore,
            MetacriticUrl = metaUrl,
            SteamReviewPercent = pct,
            SteamReviewLabel = reviews?.ReviewScoreDesc,
            SteamReviewCount = reviews?.TotalReviews,
            // Steam has no vote average; expose the review percent so sorting/cards degrade sensibly.
            VoteAverage = pct is null ? 0 : pct.Value / 10.0,
            VoteCount = reviews?.TotalReviews ?? 0
        };
    }

    private static string? FirstScreenshot(JsonElement d)
        => d.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array && shots.GetArrayLength() > 0
           && shots[0].TryGetProperty("path_full", out var full) ? full.GetString() : null;

    /// <summary>Store names carry trademark marks ("Call of Duty®: Modern Warfare® 4") that nobody types in a post.</summary>
    private static string CleanName(string name)
        => name.Replace("®", "").Replace("™", "").Replace("©", "").Replace("  ", " ").Trim();

    /// <summary>Steam dates come as "25 Oct, 2025", "Oct 2025", "Q4 2026", "2026" or "Coming soon".</summary>
    private static DateOnly? ParseSteamDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var formats = new[] { "d MMM, yyyy", "MMM d, yyyy", "MMM yyyy", "MMMM yyyy", "yyyy" };
        foreach (var f in formats)
            if (DateTime.TryParseExact(s.Trim(), f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return DateOnly.FromDateTime(dt);
        return null;
    }

    private sealed record FeaturedItem(int Id, string Name);

    private sealed class MostPlayedResponse
    {
        [JsonPropertyName("response")] public MostPlayedBody? Response { get; set; }
    }

    private sealed class MostPlayedBody
    {
        [JsonPropertyName("ranks")] public List<MostPlayedRank>? Ranks { get; set; }
    }

    private sealed class MostPlayedRank
    {
        [JsonPropertyName("appid")] public int AppId { get; set; }
    }

    private sealed class StoreSearchResponse
    {
        [JsonPropertyName("items")] public List<StoreSearchItem>? Items { get; set; }
    }

    private sealed class StoreSearchItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class ReviewsResponse
    {
        [JsonPropertyName("query_summary")] public ReviewSummary? QuerySummary { get; set; }
    }

    private sealed class ReviewSummary
    {
        [JsonPropertyName("review_score_desc")] public string? ReviewScoreDesc { get; set; }
        [JsonPropertyName("total_positive")] public int TotalPositive { get; set; }
        [JsonPropertyName("total_negative")] public int TotalNegative { get; set; }
        [JsonPropertyName("total_reviews")] public int TotalReviews { get; set; }
    }
}
