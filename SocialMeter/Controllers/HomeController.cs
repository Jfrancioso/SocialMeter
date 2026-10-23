using Microsoft.AspNetCore.Mvc;
using SocialMeter.Application.Home;
using SocialMeter.Contracts.Home;

namespace SocialMeter.Controllers;

/// <summary>The home page and the search box's suggestions. Thin: the rows, the hero and the caching live in HomeFeedService.</summary>
[ApiController]
[Route("api/home")]
public sealed class HomeController : ControllerBase
{
    private readonly HomeFeedService _feed;

    public HomeController(HomeFeedService feed) => _feed = feed;

    [HttpGet]
    public async Task<ActionResult<HomeResponseDto>> Get([FromQuery] string region = "US", CancellationToken ct = default)
    {
        // Only a two-letter country code may become a cache key; anything else collapses to the default so the cache cannot be bypassed.
        region = region.Length == 2 && region.All(char.IsAsciiLetter) ? region.ToUpperInvariant() : "US";
        return Ok(await _feed.GetHomeAsync(region, ct));
    }

    /// <summary>What the search box offers before anything is typed. Nothing about the visitor or their searches is recorded.</summary>
    [HttpGet("suggestions")]
    public async Task<ActionResult<SearchSuggestionsDto>> Suggestions(CancellationToken ct = default)
        => Ok(await _feed.GetSuggestionsAsync(ct));
}
