using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Reddit;

/// <summary>
/// Reddit posts and comments from the Arctic Shift archive, for local evaluation only (see <see cref="ArcticShiftOptions"/>).
/// Items carry platform "reddit" because that is what they are; ids are t3_/t1_ fullnames so the official source's
/// deletion sync can re-check them later.
///
/// Three passes, cheapest first, because the archive's full-text search over a big subreddit usually times out and
/// burns its per-minute budget:
///   1. The title's own subreddit (r/WarDogs, r/Severance), found by name. Read without a text filter, which answers
///      in a second or two; everything there is about the title, and the reader still judges each item.
///   2. Comments in the big general subreddits whose body names the title, window halved until the archive answers.
///   3. The "official discussion" threads those comments point at, where the best reactions never say the title's name.
/// Author account age is not in the archive, so these items carry the trust policy's unknown-account weight.
/// </summary>
public sealed partial class ArcticShiftSource : ISocialSource
{
    private static readonly string[] DedicatedSuffixes = { "", "game", "movie", "film", "tv", "show", "series", "official", "thegame", "theshow" };

    private readonly HttpClient _http;
    private readonly ArcticShiftOptions _options;
    private readonly ILogger<ArcticShiftSource> _logger;

    public ArcticShiftSource(HttpClient http, IOptions<ArcticShiftOptions> options, ILogger<ArcticShiftSource> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
    }

    public string Platform => "reddit";

    public bool IsConfigured => _options.Enabled;

    public async Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default)
    {
        if (!IsConfigured) return Array.Empty<SocialPostCandidate>();

        var name = title.Name;
        var after = DateTime.UtcNow.AddDays(-Math.Max(7, _options.Days)).ToString("yyyy-MM-dd");
        var limit = Math.Clamp(_options.Limit, 1, 100);
        var items = new Dictionary<string, Item>();

        // 1. The title's own subreddit(s).
        var dedicated = await FindDedicatedSubredditsAsync(name, ct);
        foreach (var sub in dedicated)
        {
            var (_, posts) = await SearchAsync($"api/posts/search?subreddit={Uri.EscapeDataString(sub)}&after={after}&limit={limit}&sort=desc", ct);
            foreach (var p in posts) Add(items, p, fromDedicated: true);

            var (_, comments) = await SearchAsync($"api/comments/search?subreddit={Uri.EscapeDataString(sub)}&after={after}&limit={limit}&sort=desc", ct);
            foreach (var c in comments) Add(items, c, fromDedicated: true);
        }

        // 2. Body search in the general subreddits, narrowing the window when the archive times out.
        var general = title.MediaType switch
        {
            MediaType.Tv => _options.TvSubreddits,
            MediaType.Game => _options.GameSubreddits,
            _ => _options.MovieSubreddits
        };
        var threads = new Dictionary<string, string>();
        foreach (var sub in general)
        {
            List<Item> found = new();
            for (var days = Math.Max(7, _options.Days); days >= 7; days /= 2)
            {
                var since = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
                var (ok, page) = await SearchAsync($"api/comments/search?subreddit={Uri.EscapeDataString(sub)}&body={Uri.EscapeDataString(name)}&after={since}&limit={limit}&sort=desc", ct);
                if (ok) { found = page; break; }
                if (days / 2 < 7) break;
            }
            foreach (var c in found)
            {
                if (c.Body is null || !c.Body.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                Add(items, c, fromDedicated: false);
                if (c.LinkId is not null && c.LinkTitle is not null
                    && c.LinkTitle.Contains(name, StringComparison.OrdinalIgnoreCase)
                    && c.LinkTitle.Contains("discussion", StringComparison.OrdinalIgnoreCase))
                    threads.TryAdd(c.LinkId, c.LinkTitle);
            }
        }

        // 3. The discussion threads.
        foreach (var (linkId, linkTitle) in threads.Take(Math.Max(0, _options.MaxThreads)))
        {
            var (_, found) = await SearchAsync($"api/comments/search?link_id={Uri.EscapeDataString(linkId)}&limit={limit}&sort=desc", ct);
            foreach (var c in found)
            {
                c.LinkTitle ??= linkTitle;
                Add(items, c, fromDedicated: true);
            }
        }

        var results = new List<SocialPostCandidate>(items.Count);
        foreach (var it in items.Values)
        {
            if (it.Author is null or "[deleted]" or "AutoModerator") continue;
            var text = it.Text;
            if (text is null or "[removed]" or "[deleted]" || text.Length < 8) continue;

            // Comments get their thread's title as context so the reader knows what "it" is.
            var content = it.IsComment && it.LinkTitle is not null
                ? $"[Reply in \"{Truncate(it.LinkTitle, 120)}\"]\n{Truncate(text, 1500)}"
                : Truncate(text, 1500);

            results.Add(new SocialPostCandidate(
                Platform,
                $"{(it.IsComment ? "t1_" : "t3_")}{it.Id}",
                it.Author,
                content,
                it.Score,
                it.NumComments,
                DateTimeOffset.FromUnixTimeSeconds((long)it.CreatedUtc).UtcDateTime,
                string.IsNullOrWhiteSpace(it.Permalink) ? null : "https://www.reddit.com" + it.Permalink,
                it.Subreddit,
                AuthorId: it.AuthorFullname));
        }

        _logger.LogInformation("Arctic Shift (evaluation): {Count} Reddit items for {Title}: dedicated {Dedicated}, general {General}, {Threads} discussion threads",
            results.Count, name, dedicated.Count == 0 ? "none" : string.Join('+', dedicated), string.Join('+', general), threads.Count);
        return results;
    }

    /// <summary>The archive cannot say what live Reddit has removed; the official source handles that once it is configured.</summary>
    public Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    /// <summary>
    /// Subreddits named after the title: "WarDogs", "SeveranceAppleTVPlus"-style names are matched on the compacted title plus a
    /// few common suffixes. The archive's subscriber counts are stale, so names decide, not size.
    /// </summary>
    private async Task<List<string>> FindDedicatedSubredditsAsync(string name, CancellationToken ct)
    {
        var compact = CompactRegex().Replace(name, "").ToLowerInvariant();
        if (compact.Length < 4) return new List<string>();

        var wanted = DedicatedSuffixes.Select(s => compact + s).ToHashSet();
        var found = new List<string>();
        try
        {
            using var res = await _http.GetAsync($"api/subreddits/search?subreddit_prefix={Uri.EscapeDataString(compact)}&limit=50", ct);
            var body = await res.Content.ReadFromJsonAsync<SubredditEnvelope>(cancellationToken: ct);
            foreach (var s in body?.Data ?? new List<Subreddit>())
            {
                var n = s.DisplayName ?? s.Name;
                if (n is not null && wanted.Contains(n.ToLowerInvariant())) found.Add(n);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Arctic Shift subreddit lookup failed for {Name}", name);
        }
        finally
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, _options.PauseMilliseconds)), ct);
        }
        return found.Take(2).ToList();
    }

    private static void Add(Dictionary<string, Item> items, Item it, bool fromDedicated)
    {
        if (it.Id is null) return;
        var key = (it.IsComment ? "t1_" : "t3_") + it.Id;
        if (items.ContainsKey(key)) return;
        it.FromDedicated = fromDedicated;
        items[key] = it;
    }

    /// <summary>One page of items. Ok is false when the archive timed out or errored, so the caller can narrow the window.</summary>
    private async Task<(bool Ok, List<Item> Items)> SearchAsync(string path, CancellationToken ct)
    {
        try
        {
            using var res = await _http.GetAsync(path, ct);
            if ((int)res.StatusCode == 429 || (int)res.StatusCode == 422)
            {
                // Both mean "over the per-minute budget" in practice. Honour the reset hint once, capped so a pull can't stall.
                var wait = res.Headers.TryGetValues("X-RateLimit-Reset", out var v) && double.TryParse(v.FirstOrDefault(), out var s) ? Math.Clamp(s, 2, 20) : 10;
                _logger.LogWarning("Arctic Shift {Status} for {Path}; waiting {Wait}s", (int)res.StatusCode, path, wait);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                using var retry = await _http.GetAsync(path, ct);
                var retried = await retry.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct);
                if (retry.IsSuccessStatusCode && retried?.Data is not null) return (true, retried.Data);
                _logger.LogWarning("Arctic Shift {Status} again for {Path}: {Error}", (int)retry.StatusCode, path, retried?.Error ?? "no data");
                return (false, new List<Item>());
            }

            var body = await res.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct);
            if (!res.IsSuccessStatusCode || body?.Data is null)
            {
                _logger.LogWarning("Arctic Shift {Status} for {Path}: {Error}", (int)res.StatusCode, path, body?.Error ?? "no data");
                return (false, new List<Item>());
            }
            return (true, body.Data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Arctic Shift request failed for {Path}", path);
            return (false, new List<Item>());
        }
        finally
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, _options.PauseMilliseconds)), ct);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex CompactRegex();

    private sealed class Envelope
    {
        [JsonPropertyName("data")] public List<Item>? Data { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    private sealed class SubredditEnvelope
    {
        [JsonPropertyName("data")] public List<Subreddit>? Data { get; set; }
    }

    private sealed class Subreddit
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("subscribers")] public long? Subscribers { get; set; }
    }

    /// <summary>A post or a comment; the archive uses the same field names where they overlap.</summary>
    private sealed class Item
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("selftext")] public string? SelfText { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("author_fullname")] public string? AuthorFullname { get; set; }
        [JsonPropertyName("score")] public int Score { get; set; }
        [JsonPropertyName("num_comments")] public int NumComments { get; set; }
        [JsonPropertyName("created_utc")] public double CreatedUtc { get; set; }
        [JsonPropertyName("permalink")] public string? Permalink { get; set; }
        [JsonPropertyName("subreddit")] public string? Subreddit { get; set; }
        [JsonPropertyName("link_id")] public string? LinkId { get; set; }
        [JsonPropertyName("link_title")] public string? LinkTitle { get; set; }

        [JsonIgnore] public bool IsComment => Body is not null && Title is null;
        [JsonIgnore] public bool FromDedicated { get; set; }

        /// <summary>Post: title plus self text. Comment: body.</summary>
        [JsonIgnore] public string? Text => IsComment
            ? Body
            : string.IsNullOrWhiteSpace(SelfText) ? Title : $"{Title}\n\n{SelfText}";
    }
}
