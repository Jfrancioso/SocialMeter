using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Home;

namespace SocialMeter.Controllers;

[ApiController]
[Route("api/home")]
public sealed class HomeController : ControllerBase
{
    private readonly IMovieService _movies;

    public HomeController(IMovieService movies)
    {
        _movies = movies;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string region = "US", [FromQuery] int page = 1, CancellationToken ct = default)
    {
        // In the future, you can do these in parallel with Task.WhenAll
        var trending = await _movies.GetTrendingAsync(region, page, ct);
        var nowPlaying = await _movies.GetNowPlayingAsync(region, page, ct);

        var response = new HomeResponseDto
        {
            Featured = trending.Items.FirstOrDefault(),
            Rows =
            [
                new HomeRowDto
        {
            Key = "trending",
            Title = "Trending",
            Items = trending.Items,
            Page = trending.Page,
            TotalPages = trending.TotalPages,
            TotalResults = trending.TotalResults
        },
        new HomeRowDto
        {
            Key = "now_playing",
            Title = "Now Playing",
            Items = nowPlaying.Items,
            Page = nowPlaying.Page,
            TotalPages = nowPlaying.TotalPages,
            TotalResults = nowPlaying.TotalResults
        }
            ]
        };

        return Ok(response);
    }
}
