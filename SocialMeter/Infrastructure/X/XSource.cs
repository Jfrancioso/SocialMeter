using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.X;

/// <summary>
/// Pulls recent posts from X via the v2 recent-search endpoint, expanding authors so account age and
/// follower count ride along for the trust policy. Skipped until X:BearerToken is configured.
/// </summary>
public sealed class XSource : ISocialSource
{
    private readonly HttpClient _http;
    private readonly XOptions _options;
    private readonly ILogger<XSource> _logger;

    public XSource(HttpClient http, IOptions<XOptions> options, ILogger<XSource> logger)
    {
        _http = http;
        _options = options.Value;
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

        // Replies are almost never reviews ("@grok what movie is this?"), and every post returned costs credits.
        var query = $"\"{title.Name}\" {title.MediaType.SearchWords()} -is:retweet -is:reply lang:en";
        var max = Math.Clamp(_options.MaxResults, 10, 100);

        var url = "tweets/search/recent" +
                  $"?query={Uri.EscapeDataString(query)}" +
                  $"&max_results={max}" +
                  "&tweet.fields=created_at,public_metrics,author_id" +
                  "&expansions=author_id&user.fields=username,created_at,public_metrics";

        using var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"X search failed ({(int)res.StatusCode}): {(body.Length > 200 ? body[..200] : body)}");
        }

        var payload = await res.Content.ReadFromJsonAsync<XSearchResponse>(cancellationToken: ct);
        var tweets = payload?.Data ?? new List<XTweet>();
        var users = (payload?.Includes?.Users ?? new List<XUser>())
            .Where(u => u.Id is not null)
            .ToDictionary(u => u.Id!);

        var results = new List<SocialPostCandidate>(tweets.Count);
        foreach (var t in tweets)
        {
            if (t.Id is null || string.IsNullOrWhiteSpace(t.Text)) continue;

            XUser? user = null;
            if (t.AuthorId is not null) users.TryGetValue(t.AuthorId, out user);
            var username = user?.Username ?? "unknown";

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
                AuthorReputation: user?.PublicMetrics?.FollowersCount));
        }

        _logger.LogInformation("X: {Count} posts for {Title}", results.Count, title.Name);
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
                throw new InvalidOperationException($"X lookup failed ({(int)res.StatusCode} {res.ReasonPhrase}).");

            var payload = await res.Content.ReadFromJsonAsync<XSearchResponse>(cancellationToken: ct);
            var alive = (payload?.Data ?? new List<XTweet>()).Select(t => t.Id).OfType<string>().ToHashSet();

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

    private sealed class XTweet
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        [JsonPropertyName("author_id")] public string? AuthorId { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("public_metrics")] public XMetrics? PublicMetrics { get; set; }
    }

    private sealed class XMetrics
    {
        [JsonPropertyName("like_count")] public int LikeCount { get; set; }
        [JsonPropertyName("reply_count")] public int ReplyCount { get; set; }
        [JsonPropertyName("retweet_count")] public int RetweetCount { get; set; }
    }
}
