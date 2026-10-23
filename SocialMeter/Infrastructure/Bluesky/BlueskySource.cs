using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Bluesky;

/// <summary>
/// Pulls Bluesky posts that name the title, keeps the most-liked ones that read like reactions, and looks up their
/// authors (account age and followers, for the trust policy). Reading Bluesky is free; the cost of this source is
/// Claude reading the posts it returns, which BlueskyOptions.MaxResults bounds. Skipped until an app password is set.
/// </summary>
public sealed class BlueskySource : ISocialSource
{
    private const int PageSize = 100;     // searchPosts maximum
    private const int ProfileBatch = 25;  // getProfiles and getPosts maximum

    private readonly HttpClient _http;
    private readonly BlueskySession _session;
    private readonly BlueskyOptions _options;
    private readonly TitleSearchPlanner _searches;
    private readonly ILogger<BlueskySource> _logger;

    public BlueskySource(HttpClient http, BlueskySession session, IOptions<BlueskyOptions> options, TitleSearchPlanner searches, ILogger<BlueskySource> logger)
    {
        _http = http;
        _session = session;
        _options = options.Value;
        _searches = searches;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.ServiceUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; Chatterbub/1.0; +https://chatterbub.com)");
    }

    public string Platform => BlueskyPosts.Platform;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return Array.Empty<SocialPostCandidate>();

        var search = _searches.For(title);
        if (search.Skip)
        {
            _logger.LogInformation("Bluesky: skipped {Title}: the name is an everyday word and nothing is known to pin it down", title.Name);
            return Array.Empty<SocialPostCandidate>();
        }

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(_options.SearchDays, 1, 30)).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var pages = Math.Clamp(_options.ScanLimit / PageSize, 1, 10);
        var query = Uri.EscapeDataString(BlueskyPosts.Query(search));
        var context = search.Ambiguous ? search.Context : null;

        var found = new Dictionary<string, SocialPostCandidate>();
        var scanned = 0;
        string? cursor = null;
        for (var page = 0; page < pages; page++)
        {
            var path = $"xrpc/app.bsky.feed.searchPosts?q={query}&sort=top&lang=en&since={since}&limit={PageSize}"
                       + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var doc = await GetJsonAsync(path, ct);

            var posts = doc.RootElement.TryGetProperty("posts", out var p) && p.ValueKind == JsonValueKind.Array ? p : default;
            if (posts.ValueKind != JsonValueKind.Array || posts.GetArrayLength() == 0) break;

            foreach (var post in posts.EnumerateArray())
            {
                scanned++;
                if (BlueskyPosts.ToCandidate(post, title.MediaType, _options.ExcludeTerms, context) is { } c)
                    found.TryAdd(c.ExternalId, c);
            }

            cursor = doc.RootElement.TryGetProperty("cursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (cursor is null) break;
        }

        var chosen = found.Values
            .OrderByDescending(c => c.Upvotes)
            .ThenByDescending(c => c.Comments)
            .Take(Math.Clamp(_options.MaxResults, 1, 100))
            .ToList();

        var profiles = await ProfilesAsync(chosen.Select(c => c.AuthorId).OfType<string>().Distinct().ToList(), ct);
        var results = chosen
            .Select(c => c.AuthorId is not null && profiles.TryGetValue(c.AuthorId, out var pr)
                ? c with { AuthorCreatedAt = pr.CreatedAt, AuthorReputation = pr.Followers }
                : c)
            .ToList();

        _logger.LogInformation("Bluesky: {Count} posts for {Title} (scanned {Scanned}, {Matching} matched the rules)",
            results.Count, title.Name, scanned, found.Count);
        return results;
    }

    /// <summary>
    /// Re-checks stored posts. Anything Bluesky no longer serves (deleted, taken down, or the author left or went
    /// private) is treated as removed.
    /// </summary>
    public async Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
    {
        var removed = new HashSet<string>();
        if (!IsConfigured || externalIds.Count == 0)
            return removed;

        for (var i = 0; i < externalIds.Count; i += ProfileBatch)
        {
            var chunk = externalIds.Skip(i).Take(ProfileBatch).ToList();
            var uris = chunk.Select(id => (id, uri: BlueskyPosts.UriFromExternalId(id))).ToList();
            foreach (var bad in uris.Where(u => u.uri is null)) removed.Add(bad.id);

            var valid = uris.Where(u => u.uri is not null).ToList();
            if (valid.Count == 0) continue;

            var path = "xrpc/app.bsky.feed.getPosts?" + string.Join('&', valid.Select(u => "uris=" + Uri.EscapeDataString(u.uri!)));
            using var doc = await GetJsonAsync(path, ct);
            var alive = doc.RootElement.TryGetProperty("posts", out var posts) && posts.ValueKind == JsonValueKind.Array
                ? posts.EnumerateArray()
                    .Select(p => p.TryGetProperty("uri", out var u) ? BlueskyPosts.ExternalIdFromUri(u.GetString()) : null)
                    .OfType<string>()
                    .ToHashSet()
                : new HashSet<string>();

            foreach (var (id, _) in valid.Where(u => !alive.Contains(u.id)))
                removed.Add(id);
        }

        return removed;
    }

    private sealed record Profile(DateTime? CreatedAt, int? Followers);

    private async Task<Dictionary<string, Profile>> ProfilesAsync(IReadOnlyList<string> dids, CancellationToken ct)
    {
        var profiles = new Dictionary<string, Profile>();
        for (var i = 0; i < dids.Count; i += ProfileBatch)
        {
            var chunk = dids.Skip(i).Take(ProfileBatch);
            var path = "xrpc/app.bsky.actor.getProfiles?" + string.Join('&', chunk.Select(d => "actors=" + Uri.EscapeDataString(d)));
            try
            {
                using var doc = await GetJsonAsync(path, ct);
                if (!doc.RootElement.TryGetProperty("profiles", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var p in list.EnumerateArray())
                {
                    var did = p.TryGetProperty("did", out var d) ? d.GetString() : null;
                    if (did is null) continue;
                    DateTime? created = p.TryGetProperty("createdAt", out var c) && c.ValueKind == JsonValueKind.String
                                        && DateTimeOffset.TryParse(c.GetString(), out var dt) ? dt.UtcDateTime : null;
                    int? followers = p.TryGetProperty("followersCount", out var f) && f.TryGetInt32(out var n) ? n : null;
                    profiles[did] = new Profile(created, followers);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Without profiles the posts still count, at the reduced weight the trust policy gives unknown accounts.
                _logger.LogWarning(ex, "Bluesky profile lookup failed; posts count at reduced weight");
            }
        }
        return profiles;
    }

    /// <summary>GET with the session token; an expired token is refreshed and the request retried once.</summary>
    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _session.GetAccessTokenAsync(ct));
            using var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
                return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            var tokenProblem = res.StatusCode == HttpStatusCode.Unauthorized
                               || body.Contains("ExpiredToken", StringComparison.Ordinal)
                               || body.Contains("InvalidToken", StringComparison.Ordinal);
            if (attempt == 0 && tokenProblem)
            {
                _session.Expire();
                continue;
            }

            throw new InvalidOperationException($"Bluesky request failed ({(int)res.StatusCode}): {(body.Length > 200 ? body[..200] : body)}");
        }
    }
}
