using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SocialMeter.Application.Abstractions;
using SocialMeter.Domain.Models;

namespace SocialMeter.Infrastructure.Steam;

/// <summary>
/// Steam user reviews as a social source for games. Free, keyless, and richer than any social network: each review
/// carries an explicit recommend/don't-recommend verdict, the author's playtime, and whether they bought the game.
/// Only the most recent reviews are pulled, which is the point — SocialMeter is the "right now" reading beside
/// Steam's all-time percentage.
/// </summary>
public sealed class SteamReviewSource : ISocialSource
{
    private const string StoreBase = "https://store.steampowered.com/";
    private const int PerPage = 100;
    private const int Pages = 2;

    private readonly HttpClient _http;
    private readonly ILogger<SteamReviewSource> _logger;

    public SteamReviewSource(HttpClient http, ILogger<SteamReviewSource> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SocialMeter/1.0 (+audience sentiment meter)");
    }

    public string Platform => "steam";

    public bool IsConfigured => true;

    public async Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default)
    {
        if (title.MediaType != MediaType.Game)
            return Array.Empty<SocialPostCandidate>();

        var results = new List<SocialPostCandidate>();
        var cursor = "*";

        for (var page = 0; page < Pages && cursor is not null; page++)
        {
            var url = StoreBase + $"appreviews/{title.TmdbId}?json=1&language=english&filter=recent&purchase_type=all" +
                      $"&num_per_page={PerPage}&cursor={Uri.EscapeDataString(cursor)}";
            var dto = await _http.GetFromJsonAsync<ReviewsPage>(url, ct);
            if (dto is null || dto.Success != 1 || dto.Reviews is null || dto.Reviews.Count == 0)
                break;

            foreach (var r in dto.Reviews)
            {
                if (string.IsNullOrWhiteSpace(r.RecommendationId) || string.IsNullOrWhiteSpace(r.Text)) continue;
                var steamId = r.Author?.SteamId;

                results.Add(new SocialPostCandidate(
                    Platform,
                    r.RecommendationId,
                    "Steam reviewer",
                    r.Text.Trim(),
                    r.VotesUp,
                    r.CommentCount,
                    DateTimeOffset.FromUnixTimeSeconds(r.TimestampCreated).UtcDateTime,
                    steamId is null ? null : $"https://steamcommunity.com/profiles/{steamId}/recommended/{title.TmdbId}/",
                    null,
                    AuthorId: steamId,
                    // 0 almost always means a private profile, not an empty library.
                    AuthorReputation: r.Author?.NumGamesOwned > 0 ? r.Author.NumGamesOwned : null,
                    VerdictUp: r.VotedUp,
                    EngagementMinutes: r.Author?.PlaytimeAtReview ?? r.Author?.PlaytimeForever,
                    VerifiedOwner: r.SteamPurchase,
                    ReceivedForFree: r.ReceivedForFree));
            }

            cursor = dto.Cursor == cursor ? null : dto.Cursor;
        }

        _logger.LogInformation("Steam: {Count} recent reviews for {Title}", results.Count, title.Name);
        return results;
    }

    /// <summary>
    /// Steam has no per-review lookup, so individual deletions can't be confirmed; the deletion sync instead
    /// retires Steam reviews after SocialMeter:SteamReviewRetentionDays.
    /// </summary>
    public Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    private sealed class ReviewsPage
    {
        [JsonPropertyName("success")] public int Success { get; set; }
        [JsonPropertyName("cursor")] public string? Cursor { get; set; }
        [JsonPropertyName("reviews")] public List<ReviewItem>? Reviews { get; set; }
    }

    private sealed class ReviewItem
    {
        [JsonPropertyName("recommendationid")] public string? RecommendationId { get; set; }
        [JsonPropertyName("author")] public ReviewAuthor? Author { get; set; }
        [JsonPropertyName("review")] public string? Text { get; set; }
        [JsonPropertyName("timestamp_created")] public long TimestampCreated { get; set; }
        [JsonPropertyName("voted_up")] public bool VotedUp { get; set; }
        [JsonPropertyName("votes_up")] public int VotesUp { get; set; }
        [JsonPropertyName("comment_count")] public int CommentCount { get; set; }
        [JsonPropertyName("steam_purchase")] public bool SteamPurchase { get; set; }
        [JsonPropertyName("received_for_free")] public bool ReceivedForFree { get; set; }
    }

    private sealed class ReviewAuthor
    {
        [JsonPropertyName("steamid")] public string? SteamId { get; set; }
        [JsonPropertyName("num_games_owned")] public int NumGamesOwned { get; set; }
        [JsonPropertyName("num_reviews")] public int NumReviews { get; set; }
        [JsonPropertyName("playtime_forever")] public int PlaytimeForever { get; set; }
        [JsonPropertyName("playtime_at_review")] public int? PlaytimeAtReview { get; set; }
    }
}
