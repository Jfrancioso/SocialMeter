using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Abstractions;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MoviesController : ControllerBase
{
    private readonly IMovieService _movieService;

    public MoviesController(IMovieService movieService)
    {
        _movieService = movieService;
    }

    [HttpGet("now-playing")]
    public async Task<IActionResult> GetNowPlaying([FromQuery] string region = "US", [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var movies = await _movieService.GetNowPlayingAsync(region, page, ct);
        return Ok(movies);
    }

    [HttpGet("trending")]
    public async Task<IActionResult> GetTrending([FromQuery] string region = "US", [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var movies = await _movieService.GetTrendingAsync(region, page, ct);
        return Ok(movies);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct)
    {
        var movie = await _movieService.GetByIdAsync(id, ct);
        return movie is null ? NotFound() : Ok(movie);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "Search query is required" });

        var results = await _movieService.SearchMoviesAsync(query, page, ct);
        return Ok(results);
    }

}
