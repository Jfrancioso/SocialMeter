using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Titles;
using SocialMeter.Domain.Models;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/titles")]
public sealed class TitlesController : ControllerBase
{
    private readonly ITitleService _titles;
    private readonly IRatingsService _ratings;
    private readonly ITitleTracker _tracker;

    public TitlesController(ITitleService titles, IRatingsService ratings, ITitleTracker tracker)
    {
        _titles = titles;
        _ratings = ratings;
        _tracker = tracker;
    }

    /// <summary>Search movies and shows together, or one kind with ?type=movie|tv. Each result says whether it is tracked.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<Title>>> Search([FromQuery] string query, [FromQuery] MediaType? type = null, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "query is required" });

        return Ok(await Annotated(_titles.SearchAsync(query, type, page, ct), ct));
    }

    [HttpGet("trending")]
    public async Task<ActionResult<PagedResult<Title>>> Trending([FromQuery] MediaType? type = null, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetTrendingAsync(type, page, ct), ct));

    [HttpGet("movie/now-playing")]
    public async Task<ActionResult<PagedResult<Title>>> NowPlaying([FromQuery] string region = "US", [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetNowPlayingAsync(region, page, ct), ct));

    [HttpGet("tv/on-the-air")]
    public async Task<ActionResult<PagedResult<Title>>> OnTheAir([FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetOnTheAirAsync(page, ct), ct));

    [HttpGet("game/new-releases")]
    public async Task<ActionResult<PagedResult<Title>>> NewGames([FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetNewGamesAsync(page, ct), ct));

    [HttpGet("game/coming-soon")]
    public async Task<ActionResult<PagedResult<Title>>> ComingSoonGames([FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetComingSoonGamesAsync(page, ct), ct));

    [HttpGet("{type}/popular")]
    public async Task<ActionResult<PagedResult<Title>>> Popular([FromRoute] MediaType type, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetPopularAsync(type, page, ct), ct));

    [HttpGet("{type}/top-rated")]
    public async Task<ActionResult<PagedResult<Title>>> TopRated([FromRoute] MediaType type, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await Annotated(_titles.GetTopRatedAsync(type, page, ct), ct));

    [HttpGet("{type}/{id:int}")]
    public async Task<ActionResult<Title>> Details([FromRoute] MediaType type, [FromRoute] int id, CancellationToken ct)
    {
        var title = await _titles.GetDetailsAsync(type, id, ct);
        if (title is null) return NotFound();

        await _tracker.AnnotateAsync([title], ct);
        return Ok(title);
    }

    /// <summary>The titles SocialMeter is measuring right now and why. Rebuild with POST.</summary>
    [HttpPost("tracked/refresh")]
    public async Task<ActionResult<TrackedRefreshResult>> RefreshTracked(CancellationToken ct)
        => Ok(await _tracker.RefreshAsync(ct));

    private async Task<PagedResult<Title>> Annotated(Task<PagedResult<Title>> pending, CancellationToken ct)
    {
        var result = await pending;
        await _tracker.AnnotateAsync(result.Items, ct);
        return result;
    }

    /// <summary>Ratings from the other sites, normalized to 0–100 so they sit beside the SocialMeter score.</summary>
    [HttpGet("{type}/{id:int}/ratings")]
    public async Task<ActionResult<RatingsResponseDto>> Ratings([FromRoute] MediaType type, [FromRoute] int id, CancellationToken ct)
    {
        var title = await _titles.GetDetailsAsync(type, id, ct);
        if (title is null) return NotFound();

        var ratings = await _ratings.GetRatingsAsync(title, ct);
        var available = ratings.Where(r => r.Normalized is not null).Select(r => r.Normalized!.Value).ToList();

        return Ok(new RatingsResponseDto
        {
            TmdbId = id,
            MediaType = type,
            Ratings = ratings,
            CriticsAverage = available.Count > 0 ? (int)Math.Round(available.Average()) : null
        });
    }
}
