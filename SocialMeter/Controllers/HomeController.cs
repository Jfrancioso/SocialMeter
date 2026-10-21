using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Home;
using SocialMeter.Domain.Models;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/home")]
public sealed class HomeController : ControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly ITitleService _titles;
    private readonly ITitleTracker _tracker;
    private readonly IMemoryCache _cache;

    public HomeController(ITitleService titles, ITitleTracker tracker, IMemoryCache cache)
    {
        _titles = titles;
        _tracker = tracker;
        _cache = cache;
    }

    [HttpGet]
    public async Task<ActionResult<HomeResponseDto>> Get([FromQuery] string region = "US", CancellationToken ct = default)
    {
        var key = $"home:{region.ToUpperInvariant()}";
        if (_cache.TryGetValue(key, out HomeResponseDto? cached) && cached is not null)
            return Ok(cached);

        var trendingTask = _titles.GetTrendingAsync(null, 1, ct);
        var nowPlayingTask = _titles.GetNowPlayingAsync(region, 1, ct);
        var onTheAirTask = _titles.GetOnTheAirAsync(1, ct);
        var seasonalTask = _tracker.GetSeasonalRowAsync(ct);
        var popularMoviesTask = _titles.GetPopularAsync(MediaType.Movie, 1, ct);
        var popularTvTask = _titles.GetPopularAsync(MediaType.Tv, 1, ct);

        await Task.WhenAll(trendingTask, nowPlayingTask, onTheAirTask, seasonalTask, popularMoviesTask, popularTvTask);

        var trending = trendingTask.Result;
        var (seasonLabel, seasonSubtitle, seasonItems) = seasonalTask.Result;
        var featured = trending.Items.FirstOrDefault(t => t.BackdropPath.Length > 0) ?? trending.Items.FirstOrDefault();

        var rows = new List<HomeRowDto>
        {
            new() { Key = "trending", Title = "Trending now", Subtitle = "What the internet is talking about today", Items = trending.Items },
            new() { Key = "now_playing", Title = "In theaters", Subtitle = "Playing near you", Items = nowPlayingTask.Result.Items },
            new() { Key = "seasonal", Title = seasonLabel, Subtitle = seasonSubtitle, Items = seasonItems },
            new() { Key = "on_the_air", Title = "On TV this week", Subtitle = "Shows with new episodes airing", Items = onTheAirTask.Result.Items },
            new() { Key = "popular_movies", Title = "Popular movies", Items = popularMoviesTask.Result.Items },
            new() { Key = "popular_tv", Title = "Popular shows", Items = popularTvTask.Result.Items }
        };

        await _tracker.AnnotateAsync(rows.SelectMany(r => r.Items).Concat(featured is null ? [] : [featured]), ct);

        var response = new HomeResponseDto { Featured = featured, Rows = rows };
        _cache.Set(key, response, CacheFor);
        return Ok(response);
    }
}
