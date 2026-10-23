using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Guardrails;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.X;

/// <summary>
/// Pulls recent posts from X via the v2 recent-search endpoint, expanding authors (account age, followers for the
/// trust policy) and attached media (often where the actual opinion is). Skipped until X:BearerToken is configured.
/// </summary>
public sealed class XSource : ISocialSource
{
    private readonly HttpClient _http;
    private readonly XOptions _options;
    private readonly UsageMeter _usage;
    private readonly ILogger<XSource> _logger;

    public XSource(HttpClient http, IOptions<XOptions> options, UsageMeter usage, ILogger<XSource> logger)
    {
        _http = http;
        _options = options.Value;
        _usage = usage;
        _logger = logger;

        var baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";
        _http.BaseAddress = new Uri(baseUrl);
        if (_options.IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
    }

    public string Platform => "x";

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return Array.Empty<SocialPostCandidate>();

        // Replies are almost never reviews ("@grok what movie is this?"), promoted posts are ads, and every post returned
        // costs credits. The excluded phrases are the ones that were pure promo/news in the data; see XOptions.ExcludeTerms.
        var exclusions = string.Join(' ', _options.ExcludeTerms
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.Contains(' ') ? $"-\"{t}\"" : $"-{t}"));
        var query = $"\"{title.Name}\" {title.MediaType.SearchWords()} -is:retweet -is:reply -is:nullcast lang:en {exclusions}".Trim();
        var max = Math.Clamp(_options.MaxResults, 10, 100);

        var url = "tweets/search/recent" +
                  $"?query={Uri.EscapeDataString(query)}" +
                  $"&max_results={max}" +
                  "&tweet.fields=created_at,public_metrics,author_id,possibly_sensitive,attachments" +
                  "&expansions=author_id,attachments.media_keys" +
                  "&user.fields=username,created_at,public_metrics" +
                  "&media.fields=media_key,type,url,preview_image_url,alt_text";

        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
        {
            // HttpRequestException carries the status, which is how the circuit breaker tells "out of credit" (402) and
            // "slow down" (429) from a passing hiccup. X's own explanation ("credits depleted") is kept for the log.
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"X search failed ({(int)res.StatusCode}): {(body.Length > 200 ? body[..200] : body)}", null, res.StatusCode);
        }

        var payload = await res.Content.ReadFromJsonAsync<XSearchResponse>(cancellationToken: ct);
        var tweets = payload?.Data ?? new List<XTweet>();
        _usage.RecordX(tweets.Count, payload?.Includes?.Users?.Count ?? 0);
        var users = (payload?.Includes?.Users ?? new List<XUser>())
            .Where(u => u.Id is not null)
            .ToDictionary(u => u.Id!);
        var media = (payload?.Includes?.Media ?? new List<XMedia>())
            .Where(m => m.MediaKey is not null)
            .ToDictionary(m => m.MediaKey!);

        var results = new List<SocialPostCandidate>(tweets.Count);
        foreach (var t in tweets)
        {
            if (t.Id is null || string.IsNullOrWhiteSpace(t.Text)) continue;

            XUser? user = null;
            if (t.AuthorId is not null) users.TryGetValue(t.AuthorId, out user);
            var username = user?.Username ?? "unknown";

            var attached = (t.Attachments?.MediaKeys ?? new List<string>())
                .Select(k => media.GetValueOrDefault(k))
                .OfType<XMedia>()
                .Select(m => new PostMedia(m.Url ?? m.PreviewImageUrl ?? "", m.Type ?? "photo", m.AltText))
                .Where(m => m.Url.Length > 0)
                .ToList();

            results.Add(new SocialPostCandidate(
                Platform,
                t.Id,
                username,
                // X escapes &, < and > in post text.
                WebUtility.HtmlDecode(t.Text),
                t.PublicMetrics?.LikeCount ?? 0,
                t.PublicMetrics?.ReplyCount ?? 0,
                t.CreatedAt?.UtcDateTime ?? DateTime.UtcNow,
                $"https://x.com/{username}/status/{t.Id}",
                null,
                AuthorId: t.AuthorId,
                AuthorCreatedAt: user?.CreatedAt?.UtcDateTime,
                AuthorReputation: user?.PublicMetrics?.FollowersCount,
                Media: attached.Count > 0 ? attached : null,
                Sensitive: t.PossiblySensitive));
        }

        _logger.LogInformation("X: {Count} posts for {Title} ({WithMedia} with media)", results.Count, title.Name, results.Count(r => r.Media is not null));
        return results;
    }

    /// <summary>
    /// Re-checks stored posts via the tweet lookup endpoint. Anything X no longer returns — deleted, author suspended,
    /// or made private — is treated as removed, since we have no right to keep showing it either way.
    /// </summary>
    public async Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
    {
        var removed = new HashSet<string>();
        if (!IsConfigured || externalIds.Count == 0)
            return removed;

        const int chunkSize = 100;
        for (var i = 0; i < externalIds.Count; i += chunkSize)
        {
            var chunk = externalIds.Skip(i).Take(chunkSize).ToList();
            using var res = await _http.GetAsync($"tweets?ids={string.Join(',', chunk)}&tweet.fields=id", ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"X lookup failed ({(int)res.StatusCode} {res.ReasonPhrase}).", null, res.StatusCode);

            var payload = await res.Content.ReadFromJsonAsync<XSearchResponse>(cancellationToken: ct);
            var alive = (payload?.Data ?? new List<XTweet>()).Select(t => t.Id).OfType<string>().ToHashSet();

            // X bills every post a lookup returns, so the daily deletion check is a real cost (UsageMeter counts it).
            _usage.RecordX(alive.Count, 0);

            foreach (var id in chunk.Where(id => !alive.Contains(id)))
                removed.Add(id);
        }

        return removed;
    }

    private sealed class XSearchResponse
    {
        public List<XTweet>? Data { get; set; }
        public XIncludes? Includes { get; set; }
    }

    private sealed class XIncludes
    {
        public List<XUser>? Users { get; set; }
        public List<XMedia>? Media { get; set; }
    }

    private sealed class XUser
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("public_metrics")] public XUserMetrics? PublicMetrics { get; set; }
    }

    private sealed class XUserMetrics
    {
        [JsonPropertyName("followers_count")] public int FollowersCount { get; set; }
        [JsonPropertyName("tweet_count")] public int TweetCount { get; set; }
    }

    private sealed class XMedia
    {
        [JsonPropertyName("media_key")] public string? MediaKey { get; set; }
        public string? Type { get; set; }
        public string? Url { get; set; }
        [JsonPropertyName("preview_image_url")] public string? PreviewImageUrl { get; set; }
        [JsonPropertyName("alt_text")] public string? AltText { get; set; }
    }

    private sealed class XTweet
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        [JsonPropertyName("author_id")] public string? AuthorId { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("public_metrics")] public XMetrics? PublicMetrics { get; set; }
        [JsonPropertyName("possibly_sensitive")] public bool PossiblySensitive { get; set; }
        public XAttachments? Attachments { get; set; }
    }

    private sealed class XAttachments
    {
        [JsonPropertyName("media_keys")] public List<string>? MediaKeys { get; set; }
    }

    private sealed class XMetrics
    {
        [JsonPropertyName("like_count")] public int LikeCount { get; set; }
        [JsonPropertyName("reply_count")] public int ReplyCount { get; set; }
        [JsonPropertyName("retweet_count")] public int RetweetCount { get; set; }
    }
}
