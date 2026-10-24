using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Bluesky;

/// <summary>
/// Holds one signed-in Bluesky session for the whole process. Bluesky allows only a few dozen sign-ins per account
/// every five minutes and a few hundred a day, so the app signs in once, refreshes the short-lived access token about
/// hourly with the long-lived refresh token, and signs in again only when a refresh is refused.
/// </summary>
public sealed class BlueskySession
{
    public const string HttpClientName = "bluesky-session";
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(60);

    private readonly IHttpClientFactory _http;
    private readonly BlueskyAccount _account;
    private readonly ILogger<BlueskySession> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _access;
    private string? _refresh;
    private DateTime _obtainedAt;

    /// <summary>The reading account (Bluesky:Identifier), as registered for the whole app.</summary>
    public BlueskySession(IHttpClientFactory http, IOptions<BlueskyOptions> options, ILogger<BlueskySession> logger)
        : this(http, new BlueskyAccount(options.Value.ServiceUrl, options.Value.Identifier, options.Value.AppPassword), logger)
    {
    }

    /// <summary>Any account: the brand's posting account has its own session (see BlueskyPoster).</summary>
    public BlueskySession(IHttpClientFactory http, BlueskyAccount account, ILogger<BlueskySession> logger)
    {
        _http = http;
        _account = account;
        _logger = logger;
    }

    /// <summary>The signed-in account's id (did:plc:...), known after the first sign-in; posts are written to this repo.</summary>
    public string? Did { get; private set; }

    /// <summary>A valid access token, refreshing or signing in only when needed.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_access is not null && DateTime.UtcNow - _obtainedAt < RefreshAfter)
                return _access;

            if (_refresh is not null && await TryRefreshAsync(ct))
                return _access!;

            await SignInAsync(ct);
            return _access!;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Called when Bluesky rejects the access token; the next call refreshes it.</summary>
    public void Expire() => _obtainedAt = DateTime.MinValue;

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url("com.atproto.server.refreshSession"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _refresh);
        using var res = await Client().SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogInformation("Bluesky session refresh refused ({Status}); signing in again", (int)res.StatusCode);
            _refresh = null;
            return false;
        }

        Store(await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct));
        return true;
    }

    private async Task SignInAsync(CancellationToken ct)
    {
        using var res = await Client().PostAsJsonAsync(Url("com.atproto.server.createSession"),
            new { identifier = _account.Identifier, password = _account.AppPassword }, ct);
        if (!res.IsSuccessStatusCode)
        {
            // The body names the error ("AuthenticationRequired", "RateLimitExceeded"); it never echoes the password.
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Bluesky sign-in failed ({(int)res.StatusCode}): {(body.Length > 200 ? body[..200] : body)}");
        }

        Store(await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct));
        _logger.LogInformation("Bluesky: signed in as {Handle}", _account.Identifier);
    }

    private void Store(JsonElement session)
    {
        _access = session.GetProperty("accessJwt").GetString();
        _refresh = session.GetProperty("refreshJwt").GetString();
        if (session.TryGetProperty("did", out var did)) Did = did.GetString();
        _obtainedAt = DateTime.UtcNow;
    }

    private HttpClient Client()
    {
        var client = _http.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private string Url(string method) => $"{_account.ServiceUrl.TrimEnd('/')}/xrpc/{method}";
}

/// <summary>A Bluesky account the app signs in as: its server, handle and an app password (never the account password).</summary>
public sealed record BlueskyAccount(string ServiceUrl, string Identifier, string AppPassword);
