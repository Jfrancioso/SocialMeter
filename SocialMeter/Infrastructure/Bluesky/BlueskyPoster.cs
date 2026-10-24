using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Bluesky;

/// <summary>
/// Posts as the brand's Bluesky account with its own signed-in session (Posting:Bluesky), separate from the account the
/// site reads with. Writes a thread: the post with its link card, then each quote as a reply under the one before.
/// </summary>
public sealed class BlueskyPoster : IBlueskyPoster
{
    /// <summary>Bluesky refuses blobs over about a megabyte; link-card images are kept well under.</summary>
    private const int MaxImageBytes = 950_000;

    private readonly IHttpClientFactory _http;
    private readonly PostingOptions _options;
    private readonly ILogger<BlueskyPoster> _logger;
    private readonly BlueskySession? _session;

    public BlueskyPoster(IHttpClientFactory http, IOptions<PostingOptions> options, ILogger<BlueskySession> sessionLogger, ILogger<BlueskyPoster> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        var account = _options.Bluesky;
        if (account.IsConfigured)
            _session = new BlueskySession(http, new BlueskyAccount(account.ServiceUrl, account.Identifier.Trim(), account.AppPassword), sessionLogger);
    }

    public bool IsConfigured => _session is not null;
    public string? Account => IsConfigured ? _options.Bluesky.Identifier.Trim() : null;

    public async Task<BlueskyPostResult> PostThreadAsync(BlueskyThread thread, CancellationToken ct = default)
    {
        if (_session is null) throw new InvalidOperationException("The brand's Bluesky account isn't set up (Posting:Bluesky).");

        var token = await _session.GetAccessTokenAsync(ct);
        var did = _session.Did ?? throw new InvalidOperationException("Bluesky sign-in returned no account id.");
        var now = DateTime.UtcNow;

        var thumb = await UploadImageAsync(thread.Card.ImageUrl, token, ct);
        var post = BlueskyRecords.Post(thread.Text, thread.LinkText, thread.Card.Url, thread.Card.Title, thread.Card.Description, thumb, now);
        var root = await CreateAsync(did, post, token, ct);

        int posted = 0, skipped = 0;
        var parent = root;
        foreach (var quote in thread.Quotes)
        {
            try
            {
                var quoted = await QuotableAsync(quote.AtUri, token, ct);
                if (quoted is null) { skipped++; continue; }
                parent = await CreateAsync(did, BlueskyRecords.QuoteReply(quote.Lead, root, parent, quoted.Value, DateTime.UtcNow), token, ct);
                posted++;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                // The post is already up; a missing reply is better than failing the whole thing and posting it twice.
                _logger.LogWarning("Bluesky: quote reply not posted ({Error})", ex.GetType().Name);
                skipped++;
            }
        }

        _logger.LogInformation("Bluesky: posted a thread as {Handle} ({Quotes} quotes, {Skipped} skipped)", Account, posted, skipped);
        return new BlueskyPostResult(root.Uri, BlueskyRecords.WebUrl(root.Uri), posted, skipped);
    }

    /// <summary>The quoted post's address and version (cid), or null when it's gone or its author has turned off quoting.</summary>
    private async Task<(string Uri, string Cid)?> QuotableAsync(string atUri, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url($"app.bsky.feed.getPosts?uris={Uri.EscapeDataString(atUri)}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await Client().SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var body = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!body.TryGetProperty("posts", out var posts) || posts.GetArrayLength() == 0) return null;
        var view = posts[0];
        if (view.TryGetProperty("viewer", out var viewer) && viewer.TryGetProperty("embeddingDisabled", out var off) && off.ValueKind == JsonValueKind.True)
            return null;
        return (view.GetProperty("uri").GetString()!, view.GetProperty("cid").GetString()!);
    }

    private async Task<(string Uri, string Cid)> CreateAsync(string did, JsonObject record, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url("com.atproto.repo.createRecord"))
        {
            Content = JsonContent.Create(new JsonObject { ["repo"] = did, ["collection"] = BlueskyRecords.PostCollection, ["record"] = record })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await Client().SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) _session!.Expire();
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Bluesky refused the post ({(int)res.StatusCode}): {(text.Length > 160 ? text[..160] : text)}");
        }
        var created = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return (created.GetProperty("uri").GetString()!, created.GetProperty("cid").GetString()!);
    }

    /// <summary>Uploads the card image; null (a card without an image) when there is none or it can't be fetched.</summary>
    private async Task<JsonNode?> UploadImageAsync(string? imageUrl, string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(imageUrl)) return null;
        try
        {
            using var image = await Client().GetAsync(imageUrl, ct);
            var type = image.Content.Headers.ContentType?.MediaType;
            if (!image.IsSuccessStatusCode || type is not ("image/jpeg" or "image/png")) return null;
            var bytes = await image.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is 0 or > MaxImageBytes) return null;

            using var req = new HttpRequestMessage(HttpMethod.Post, Url("com.atproto.repo.uploadBlob")) { Content = new ByteArrayContent(bytes) };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await Client().SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
            return body?["blob"];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Bluesky: card image not uploaded ({Error})", ex.GetType().Name);
            return null;
        }
    }

    private HttpClient Client()
    {
        var client = _http.CreateClient(BlueskySession.HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private string Url(string method) => $"{_options.Bluesky.ServiceUrl.TrimEnd('/')}/xrpc/{method}";
}
