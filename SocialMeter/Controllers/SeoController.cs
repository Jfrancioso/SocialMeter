using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Application.Abstractions;
using SocialMeter.Contracts.Seo;

namespace SocialMeter.Controllers;

/// <summary>
/// What the edge Worker needs to build chatterbub.com/sitemap.xml: the title pages that have a reading, newest first.
/// Titles without one show only "Not tracked" or "Not read yet", so they are left out rather than offered to search engines.
/// </summary>
[ApiController]
[Route("api/seo")]
public sealed class SeoController : ControllerBase
{
    private const int MaxTitles = 5000;   // well under the sitemap protocol's 50,000 URLs per file
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    private readonly IPostQueries _queries;
    private readonly IMemoryCache _cache;

    public SeoController(IPostQueries queries, IMemoryCache cache)
    {
        _queries = queries;
        _cache = cache;
    }

    [HttpGet("titles")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<IReadOnlyList<SitemapTitleDto>>> Titles(CancellationToken ct = default)
    {
        var titles = await _cache.GetOrCreateAsync("seo:titles", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return _queries.GetScoredTitlesAsync(MaxTitles, ct);
        });
        return Ok(titles);
    }
}
