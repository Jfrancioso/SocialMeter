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

    public TitlesController(ITitleService titles, IRatingsService ratings)
    {
        _titles = titles;
        _ratings = ratings;
    }

    /// <summary>Search movies and shows together, or one kind with ?type=movie|tv.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<Title>>> Search([FromQuery] string query, [FromQuery] MediaType? type = null, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "query is required" });

        return Ok(await _titles.SearchAsync(query, type, page, ct));
    }

    [HttpGet("trending")]
    public async Task<ActionResult<PagedResult<Title>>> Trending([FromQuery] MediaType? type = null, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _titles.GetTrendingAsync(type, page, ct));

    [HttpGet("movie/now-playing")]
    public async Task<ActionResult<PagedResult<Title>>> NowPlaying([FromQuery] string region = "US", [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _titles.GetNowPlayingAsync(region, page, ct));

    [HttpGet("tv/on-the-air")]
    public async Task<ActionResult<PagedResult<Title>>> OnTheAir([FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _titles.GetOnTheAirAsync(page, ct));

    [HttpGet("{type}/popular")]
    public async Task<ActionResult<PagedResult<Title>>> Popular([FromRoute] MediaType type, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _titles.GetPopularAsync(type, page, ct));

    [HttpGet("{type}/top-rated")]
    public async Task<ActionResult<PagedResult<Title>>> TopRated([FromRoute] MediaType type, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _titles.GetTopRatedAsync(type, page, ct));

    [HttpGet("{type}/{id:int}")]
    public async Task<ActionResult<Title>> Details([FromRoute] MediaType type, [FromRoute] int id, CancellationToken ct)
    {
        var title = await _titles.GetDetailsAsync(type, id, ct);
        return title is null ? NotFound() : Ok(title);
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
