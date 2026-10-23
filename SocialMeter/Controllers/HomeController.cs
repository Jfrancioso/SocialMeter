using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Home;
using SocialMeter.Contracts.Home;
using SocialMeter.Domain.Models;
using SocialMeter.Options;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/home")]
public sealed class HomeController : ControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan FeaturedCacheFor = TimeSpan.FromMinutes(15);

    private readonly ITitleService _titles;
    private readonly ITitleTracker _tracker;
    private readonly IPostQueries _queries;
    private readonly SocialMeterOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ITitleService titles, ITitleTracker tracker, IPostQueries queries, IOptions<SocialMeterOptions> options,
        IMemoryCache cache, ILogger<HomeController> logger)
    {
        _titles = titles;
        _tracker = tracker;
        _queries = queries;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<HomeResponseDto>> Get([FromQuery] string region = "US", CancellationToken ct = default)
    {
        // Only a two-letter country code may become a cache key; anything else collapses to the default so the cache cannot be bypassed.
        region = region.Length == 2 && region.All(char.IsAsciiLetter) ? region.ToUpperInvariant() : "US";
        var key = $"home:{region}";
        if (_cache.TryGetValue(key, out HomeResponseDto? cached) && cached is not null)
            return Ok(cached);

        var trendingTask = _titles.GetTrendingAsync(null, 1, ct);
        var nowPlayingTask = _titles.GetNowPlayingAsync(region, 1, ct);
        var onTheAirTask = _titles.GetOnTheAirAsync(1, ct);
        var seasonalTask = _tracker.GetSeasonalRowAsync(ct);
        var seasonalTopTask = _tracker.GetSeasonalTopRowsAsync(region, ct);
        var popularMoviesTask = _titles.GetPopularAsync(MediaType.Movie, 1, ct);
        var popularTvTask = _titles.GetPopularAsync(MediaType.Tv, 1, ct);
        var topGamesTask = SafeAsync(_titles.GetTrendingAsync(MediaType.Game, 1, ct));
        var mostPlayedTask = SafeAsync(_titles.GetPopularAsync(MediaType.Game, 1, ct));

        await Task.WhenAll(trendingTask, nowPlayingTask, onTheAirTask, seasonalTask, seasonalTopTask, popularMoviesTask, popularTvTask, topGamesTask, mostPlayedTask);

        var trending = trendingTask.Result;
        var (seasonLabel, seasonSubtitle, seasonItems) = seasonalTask.Result;
        var (featured, featuredLabel, featuredPosts) = await PickFeaturedAsync(trending.Items, ct);

        // Seasonal top rows (October's horror pair) come before everything else; the rest of the page keeps its order.
        var rows = seasonalTopTask.Result
            .Select(r => new HomeRowDto { Key = r.Key, Title = r.Title, Subtitle = r.Subtitle, Items = r.Items })
            .ToList();
        rows.AddRange(new List<HomeRowDto>
        {
            new() { Key = "trending", Title = "Trending now", Subtitle = "What the internet is talking about today", Items = trending.Items },
            new() { Key = "now_playing", Title = "In theaters", Subtitle = "Playing near you", Items = nowPlayingTask.Result.Items },
            new() { Key = "seasonal", Title = seasonLabel, Subtitle = seasonSubtitle, Items = seasonItems },
            new() { Key = "on_the_air", Title = "On TV this week", Subtitle = "Shows with new episodes airing", Items = onTheAirTask.Result.Items },
            new() { Key = "top_games", Title = "Hot on Steam", Subtitle = "This week's top sellers", Items = topGamesTask.Result },
            new() { Key = "popular_movies", Title = "Popular movies", Items = popularMoviesTask.Result.Items },
            new() { Key = "popular_tv", Title = "Popular shows", Items = popularTvTask.Result.Items },
            new() { Key = "most_played", Title = "Most played right now", Subtitle = "Steam's live player counts", Items = mostPlayedTask.Result }
        });

        await _tracker.AnnotateAsync(rows.SelectMany(r => r.Items).Concat(featured is null ? [] : [featured]), ct);

        var response = new HomeResponseDto
        {
            Featured = featured,
            FeaturedLabel = featuredLabel,
            FeaturedPosts = featuredPosts,
            FeaturedWindowDays = _options.FeaturedWindowDays,
            Rows = rows.Where(r => r.Items.Count > 0).ToList()
        };
        _cache.Set(key, response, CacheFor);
        return Ok(response);
    }

    /// <summary>
    /// The hero: the tracked movie or show people are actually posting about (see <see cref="FeaturedPick"/>), else TMDB's
    /// trending pick, labelled as TMDB's. The post counts change only when titles are pulled, so they are cached briefly.
    /// </summary>
    private async Task<(Title? Title, string Label, int? Posts)> PickFeaturedAsync(IReadOnlyList<Title> trending, CancellationToken ct)
    {
        try
        {
            var candidates = await _cache.GetOrCreateAsync("home:most-talked-about", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = FeaturedCacheFor;
                var since = DateTime.UtcNow.AddDays(-Math.Clamp(_options.FeaturedWindowDays, 1, 14));
                return _queries.GetMostTalkedAboutAsync(since, Math.Max(1, _options.FeaturedMinPosts), 20, ct);
            }) ?? Array.Empty<TalkedAbout>();

            var tracked = new HashSet<(MediaType, int)>();
            foreach (var c in candidates)
                if (await _tracker.GetReasonAsync(c.MediaType, c.TmdbId, ct) is not null)
                    tracked.Add((c.MediaType, c.TmdbId));

            foreach (var pick in FeaturedPick.Eligible(candidates, c => tracked.Contains((c.MediaType, c.TmdbId)),
                         _options.FeaturedMinPosts, _options.FeaturedMinConfidence))
            {
                var title = await _titles.GetDetailsAsync(pick.MediaType, pick.TmdbId, ct);
                if (title is { BackdropPath.Length: > 0 })
                    return (title, FeaturedPick.MostTalkedAbout, pick.Posts);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A slow or paused database must not take the home page down; TMDB's pick still works.
            _logger.LogWarning(ex, "Most-talked-about lookup failed; featuring TMDB's trending pick");
        }

        var fallback = trending.FirstOrDefault(t => t.BackdropPath.Length > 0) ?? trending.FirstOrDefault();
        return (fallback, FeaturedPick.TrendingOnTmdb, null);
    }

    /// <summary>Steam is a second provider; if it is down the home page should still render the TMDB rows.</summary>
    private static async Task<IReadOnlyList<Title>> SafeAsync(Task<PagedResult<Title>> pending)
    {
        try { return (await pending).Items; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Array.Empty<Title>(); }
    }
}
