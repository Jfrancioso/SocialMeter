using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Reddit;

/// <summary>
/// Reddit comments from the Arctic Shift archive, for local evaluation only (see <see cref="ArcticShiftOptions"/>).
/// Posts carry platform "reddit" because that is what they are; ids are t1_ fullnames so the official source's
/// deletion sync can re-check them later. Two passes per subreddit: comments whose body names the title, then the
/// discussion threads those comments live in, because "loved it, the third act is incredible" under an
/// "Official Discussion - Whalefall" thread never says "Whalefall". Author account age is not in the archive, so these
/// posts carry the trust policy's unknown-account weight.
/// </summary>
public sealed class ArcticShiftSource : ISocialSource
{
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

        var subreddits = title.MediaType switch
        {
            MediaType.Tv => _options.TvSubreddits,
            MediaType.Game => _options.GameSubreddits,
            _ => _options.MovieSubreddits
        };
        var limit = Math.Clamp(_options.Limit, 1, 100);
        var name = title.Name;

        var byId = new Dictionary<string, Comment>();
        var threads = new Dictionary<string, string>(); // link_id -> link title

        foreach (var sub in subreddits)
        {
            // Full-text search over a big subreddit times out on the archive when the window is wide; halve it until it answers.
            List<Comment> found = new();
            for (var days = Math.Max(7, _options.Days); days >= 7; days /= 2)
            {
                var after = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
                var path = $"api/comments/search?subreddit={Uri.EscapeDataString(sub)}&body={Uri.EscapeDataString(name)}&after={after}&limit={limit}&sort=desc";
                var (ok, page) = await SearchAsync(path, ct);
                if (!ok)
                {
                    // A sub-second "timeout" is the archive rate-limiting us, not a slow query: wait, then try the same window once more.
                    await Task.Delay(TimeSpan.FromSeconds(4), ct);
                    (ok, page) = await SearchAsync(path, ct);
                }
                if (ok) { found = page; break; }
                if (days / 2 < 7) break;
            }
            foreach (var c in found)
            {
                if (c.Id is null || c.Body is null) continue;
                if (!c.Body.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                byId[c.Id] = c;

                // A thread whose title names this title and reads like a discussion is worth expanding.
                if (c.LinkId is not null && c.LinkTitle is not null
                    && c.LinkTitle.Contains(name, StringComparison.OrdinalIgnoreCase)
                    && c.LinkTitle.Contains("discussion", StringComparison.OrdinalIgnoreCase))
                    threads.TryAdd(c.LinkId, c.LinkTitle);
            }
        }

        foreach (var (linkId, linkTitle) in threads.Take(Math.Max(0, _options.MaxThreads)))
        {
            var (_, found) = await SearchAsync($"api/comments/search?link_id={Uri.EscapeDataString(linkId)}&limit={limit}&sort=desc", ct);
            foreach (var c in found)
            {
                if (c.Id is null || c.Body is null || byId.ContainsKey(c.Id)) continue;
                if (c.Body is "[removed]" or "[deleted]" || c.Body.Length < 8) continue;
                c.LinkTitle ??= linkTitle;
                c.FromThread = true;
                byId[c.Id] = c;
            }
        }

        var results = new List<SocialPostCandidate>(byId.Count);
        foreach (var c in byId.Values)
        {
            if (c.Author is null or "[deleted]" or "AutoModerator") continue;

            // Thread comments get the thread title as context so the reader knows what "it" is.
            var content = c.FromThread && c.LinkTitle is not null
                ? $"[Reply in \"{Truncate(c.LinkTitle, 120)}\"]\n{Truncate(c.Body!, 1500)}"
                : Truncate(c.Body!, 1500);

            results.Add(new SocialPostCandidate(
                Platform,
                $"t1_{c.Id}",
                c.Author,
                content,
                c.Score,
                0,
                DateTimeOffset.FromUnixTimeSeconds((long)c.CreatedUtc).UtcDateTime,
                string.IsNullOrWhiteSpace(c.Permalink) ? null : "https://www.reddit.com" + c.Permalink,
                c.Subreddit,
                AuthorId: c.AuthorFullname));
        }

        _logger.LogInformation("Arctic Shift (evaluation): {Count} Reddit comments for {Title} from {Subs} subreddits, {Threads} discussion threads",
            results.Count, name, subreddits.Count, threads.Count);
        return results;
    }

    /// <summary>The archive cannot say what live Reddit has removed; the official source handles that once it is configured.</summary>
    public Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    /// <summary>One page of comments. Ok is false when the archive timed out or errored, so the caller can narrow the window and retry.</summary>
    private async Task<(bool Ok, List<Comment> Comments)> SearchAsync(string path, CancellationToken ct)
    {
        try
        {
            using var res = await _http.GetAsync(path, ct);
            if ((int)res.StatusCode == 429)
            {
                // Honour the archive's reset hint (seconds), capped so a bad header can't stall a pull.
                var wait = res.Headers.TryGetValues("X-RateLimit-Reset", out var v) && double.TryParse(v.FirstOrDefault(), out var s) ? Math.Clamp(s, 2, 20) : 10;
                _logger.LogWarning("Arctic Shift 429 for {Path}; waiting {Wait}s", path, wait);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                using var retry = await _http.GetAsync(path, ct);
                var retried = await retry.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct);
                return retry.IsSuccessStatusCode && retried?.Data is not null ? (true, retried.Data) : (false, new List<Comment>());
            }

            var body = await res.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct);
            if (!res.IsSuccessStatusCode || body?.Data is null)
            {
                // The archive answers 422 "Timeout. Maybe slow down a bit" when a search is too wide for its current load.
                _logger.LogWarning("Arctic Shift {Status} for {Path}: {Error}", (int)res.StatusCode, path, body?.Error ?? "no data");
                return (false, new List<Comment>());
            }
            return (true, body.Data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Arctic Shift request failed for {Path}", path);
            return (false, new List<Comment>());
        }
        finally
        {
            // Rate limits are dynamic; a pause between calls keeps a title's handful of searches under them.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, _options.PauseMilliseconds)), ct);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class Envelope
    {
        [JsonPropertyName("data")] public List<Comment>? Data { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    private sealed class Comment
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("author_fullname")] public string? AuthorFullname { get; set; }
        [JsonPropertyName("score")] public int Score { get; set; }
        [JsonPropertyName("created_utc")] public double CreatedUtc { get; set; }
        [JsonPropertyName("permalink")] public string? Permalink { get; set; }
        [JsonPropertyName("subreddit")] public string? Subreddit { get; set; }
        [JsonPropertyName("link_id")] public string? LinkId { get; set; }
        [JsonPropertyName("link_title")] public string? LinkTitle { get; set; }
        [JsonIgnore] public bool FromThread { get; set; }
    }
}
