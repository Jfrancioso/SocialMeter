using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Reddit;

/// <summary>
/// Pulls Reddit posts that discuss a title through the OAuth API (client-credentials flow for a "script" app),
/// then looks up each author's account age and karma so the trust policy can weigh them.
/// Reddit returns 403 to unauthenticated JSON requests, so the source is skipped until credentials are configured.
/// </summary>
public sealed class RedditSource : ISocialSource
{
    private const string OAuthBase = "https://oauth.reddit.com/";
    private const string TokenUrl = "https://www.reddit.com/api/v1/access_token";
    private const string TokenCacheKey = "reddit:token";
    private const int UserLookupChunk = 100;

    private readonly HttpClient _http;
    private readonly RedditOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RedditSource> _logger;

    public RedditSource(HttpClient http, IOptions<RedditOptions> options, IMemoryCache cache, ILogger<RedditSource> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        // Reddit wants a "platform:app:version (by /u/name)" agent, which is not a valid product token, so skip validation.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
    }

    public string Platform => "reddit";

    public bool IsConfigured => _options.HasCredentials;

    public async Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return Array.Empty<SocialPostCandidate>();

        var query = BuildQuery(title);
        var limit = Math.Clamp(_options.Limit, 1, 100);
        var path = $"search?q={Uri.EscapeDataString(query)}&sort=relevance&t=year&limit={limit}&type=link&raw_json=1";

        var listing = await GetAsync<RedditListing>(path, ct);
        var children = listing?.Data?.Children ?? new List<RedditChild>();

        var name = title.Name;
        var kept = new List<RedditPost>();
        foreach (var child in children)
        {
            var d = child.Data;
            if (d is null || string.IsNullOrWhiteSpace(d.Id)) continue;
            if (!Mentions(d.Title, name) && !Mentions(d.SelfText, name)) continue;
            kept.Add(d);
        }

        var authors = await LookupAuthorsAsync(kept.Select(p => p.AuthorFullname).OfType<string>().Distinct().ToList(), ct);

        var results = new List<SocialPostCandidate>(kept.Count);
        foreach (var d in kept)
        {
            var content = string.IsNullOrWhiteSpace(d.SelfText)
                ? d.Title ?? ""
                : $"{d.Title}\n\n{Truncate(d.SelfText!, 1500)}";

            RedditUser? user = null;
            if (d.AuthorFullname is not null) authors.TryGetValue(d.AuthorFullname, out user);

            results.Add(new SocialPostCandidate(
                Platform,
                d.Id!,
                d.Author ?? "[deleted]",
                content,
                d.Score,
                d.NumComments,
                DateTimeOffset.FromUnixTimeSeconds((long)d.CreatedUtc).UtcDateTime,
                string.IsNullOrWhiteSpace(d.Permalink) ? null : "https://www.reddit.com" + d.Permalink,
                d.Subreddit,
                AuthorId: d.AuthorFullname,
                AuthorCreatedAt: user is null ? null : DateTimeOffset.FromUnixTimeSeconds((long)user.CreatedUtc).UtcDateTime,
                AuthorReputation: user is null ? null : user.LinkKarma + user.CommentKarma));
        }

        _logger.LogInformation("Reddit: {Kept}/{Total} posts kept for {Title}, {Authors} authors resolved", results.Count, children.Count, name, authors.Count);
        return results;
    }

    /// <summary>
    /// Re-checks stored posts via /api/info. A post counts as removed when Reddit no longer returns it, the author
    /// deleted it, or a moderator/admin removed it — in every case the content is gone from Reddit and must go here too.
    /// </summary>
    public async Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
    {
        var removed = new HashSet<string>();
        if (!IsConfigured || externalIds.Count == 0)
            return removed;

        for (var i = 0; i < externalIds.Count; i += UserLookupChunk)
        {
            var chunk = externalIds.Skip(i).Take(UserLookupChunk).ToList();
            var fullnames = string.Join(',', chunk.Select(id => $"t3_{id}"));
            var listing = await GetAsync<RedditListing>($"api/info?id={Uri.EscapeDataString(fullnames)}&raw_json=1", ct);

            var alive = new HashSet<string>();
            foreach (var child in listing?.Data?.Children ?? new List<RedditChild>())
            {
                var d = child.Data;
                if (d?.Id is null) continue;

                var gone = d.Author is null or "[deleted]"
                           || d.RemovedByCategory is not null
                           || d.SelfText is "[removed]" or "[deleted]";
                if (!gone) alive.Add(d.Id);
            }

            foreach (var id in chunk.Where(id => !alive.Contains(id)))
                removed.Add(id);
        }

        return removed;
    }

    /// <summary>Account age and karma for a batch of t2_ fullnames. Failures degrade to "unknown" rather than failing the fetch.</summary>
    private async Task<Dictionary<string, RedditUser>> LookupAuthorsAsync(List<string> fullnames, CancellationToken ct)
    {
        var result = new Dictionary<string, RedditUser>();
        for (var i = 0; i < fullnames.Count; i += UserLookupChunk)
        {
            var chunk = fullnames.Skip(i).Take(UserLookupChunk);
            try
            {
                var users = await GetAsync<Dictionary<string, RedditUser>>($"api/user_data_by_account_ids?ids={string.Join(',', chunk)}", ct);
                if (users is null) continue;
                foreach (var (id, user) in users) result[id] = user;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reddit author lookup failed; those posts will carry unknown account age");
            }
        }
        return result;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, OAuthBase + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Reddit request failed ({(int)res.StatusCode} {res.ReasonPhrase}).");

        return await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private static string BuildQuery(Title title)
    {
        // Quoted title keeps the search tight; the media words push out namesakes of the other kinds.
        return $"\"{title.Name}\" {title.MediaType.SearchWords()}";
    }

    private static bool Mentions(string? text, string name)
        => !string.IsNullOrEmpty(text) && text.Contains(name, StringComparison.OrdinalIgnoreCase);

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null)
            return cached;

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Reddit token request failed ({(int)res.StatusCode}). Check Reddit:ClientId / ClientSecret.");

        var token = await res.Content.ReadFromJsonAsync<RedditToken>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Reddit token response was empty.");

        var accessToken = token.AccessToken ?? throw new InvalidOperationException("Reddit token response had no access_token.");
        _cache.Set(TokenCacheKey, accessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 60)));
        return accessToken;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class RedditToken
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class RedditListing
    {
        public RedditListingData? Data { get; set; }
    }

    private sealed class RedditListingData
    {
        public List<RedditChild> Children { get; set; } = new();
    }

    private sealed class RedditChild
    {
        public RedditPost? Data { get; set; }
    }

    private sealed class RedditPost
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        [JsonPropertyName("selftext")] public string? SelfText { get; set; }
        public string? Author { get; set; }
        [JsonPropertyName("author_fullname")] public string? AuthorFullname { get; set; }
        [JsonPropertyName("removed_by_category")] public string? RemovedByCategory { get; set; }
        public int Score { get; set; }
        [JsonPropertyName("num_comments")] public int NumComments { get; set; }
        [JsonPropertyName("created_utc")] public double CreatedUtc { get; set; }
        public string? Permalink { get; set; }
        public string? Subreddit { get; set; }
    }

    private sealed class RedditUser
    {
        public string? Name { get; set; }
        [JsonPropertyName("created_utc")] public double CreatedUtc { get; set; }
        [JsonPropertyName("link_karma")] public int LinkKarma { get; set; }
        [JsonPropertyName("comment_karma")] public int CommentKarma { get; set; }
    }
}
