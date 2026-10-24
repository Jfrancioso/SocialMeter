using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Common;
using SocialMeter.Application.Guardrails;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Options;
using SocialMeter.Web;
using SocialMeter.Web.Filters;

namespace SocialMeter.Controllers;

/// <summary>Readings, explanations, history and posts for a title. Thin: every action is one call into the application layer.</summary>
[ApiController]
[Route("api/socialmeter")]
public sealed class SocialMeterController : ControllerBase
{
    private readonly ISocialMeterService _meter;
    private readonly IScoreExplainer _explainer;
    private readonly ITitleHighlights _highlights;
    private readonly ICrowdShape _crowd;
    private readonly IPostQueries _queries;
    private readonly LivePullBudget _budget;

    public SocialMeterController(ISocialMeterService meter, IScoreExplainer explainer, ITitleHighlights highlights, ICrowdShape crowd,
        IPostQueries queries, LivePullBudget budget)
    {
        _meter = meter;
        _explainer = explainer;
        _highlights = highlights;
        _crowd = crowd;
        _queries = queries;
        _budget = budget;
    }

    /// <summary>
    /// The SocialMeter reading for a title. ?refresh=true asks for a live pull now; each caller gets a few per hour and the
    /// process has an hourly ceiling, beyond which the cached reading is served.
    /// </summary>
    [HttpGet("{type}/{id:int}")]
    public async Task<ActionResult<SocialMeterResponseDto>> Get([FromRoute] MediaType type, [FromRoute] int id, [FromQuery] bool refresh = false, CancellationToken ct = default)
    {
        if (refresh && !_budget.TryAcquireForcedRefresh(ClientAddress.For(HttpContext)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Status = 429,
                Title = "Too many live refreshes",
                Detail = $"Each reader gets {_budget.ForcedRefreshesPerHour} live refreshes an hour. The cached reading is still available without ?refresh."
            });
        }

        var result = await _meter.GetScoreAsync(type, id, refresh ? ReadTrigger.Button : ReadTrigger.Page, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Why the reading is what it is: effective posts, margin, weight by sentiment and substance, the posts that pull hardest.</summary>
    [HttpGet("{type}/{id:int}/explain")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<ScoreExplanationDto>> Explain([FromRoute] MediaType type, [FromRoute] int id, CancellationToken ct = default)
    {
        var result = await _explainer.ExplainAsync(type, id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>"In their words": the featured positive and negative post, with each side's size. Both null with no counted posts.</summary>
    [HttpGet("{type}/{id:int}/highlights")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<TitleHighlightsDto>> Highlights([FromRoute] MediaType type, [FromRoute] int id, CancellationToken ct = default)
        => Ok(await _highlights.GetAsync(type, id, ct));

    /// <summary>"The crowd" (pilot titles only, CrowdShape:Titles): every counted post as a dot, no text. Enabled=false elsewhere.</summary>
    [HttpGet("{type}/{id:int}/crowd")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<CrowdShapeDto>> Crowd([FromRoute] MediaType type, [FromRoute] int id, CancellationToken ct = default)
        => Ok(await _crowd.GetAsync(type, id, ct));

    /// <summary>One counted post of a title, for a dot clicked in "The crowd". 404 for a hidden, filtered or other title's post.</summary>
    [HttpGet("{type}/{id:int}/posts/{postId:int}")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<SocialPostDto>> Post([FromRoute] MediaType type, [FromRoute] int id, [FromRoute] int postId, CancellationToken ct = default)
    {
        var post = await _queries.GetPostAsync(type, id, postId, ct);
        return post is null ? NotFound() : Ok(post);
    }

    /// <summary>One reading per day plus the Mann-Kendall trend verdict over the configured window.</summary>
    [HttpGet("{type}/{id:int}/history")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<ScoreHistoryDto>> History([FromRoute] MediaType type, [FromRoute] int id, [FromQuery] int days = 30, CancellationToken ct = default)
        => Ok(await _queries.GetHistoryAsync(type, id, days, ct));

    [HttpGet("{type}/{id:int}/posts")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<PagedResult<SocialPostDto>>> Posts(
        [FromRoute] MediaType type,
        [FromRoute] int id,
        [FromQuery] Sentiment? sentiment = null,
        [FromQuery] string? platform = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] PostSort sort = PostSort.Top,
        CancellationToken ct = default)
        => Ok(await _queries.GetPostsAsync(type, id, sentiment, platform, page, pageSize, sort, ct));

    /// <summary>Cached readings for many titles at once, e.g. ?keys=movie:693134,tv:1399. Never contacts the sources.</summary>
    [HttpGet("snapshots")]
    [EnableRateLimiting(Web.DependencyInjection.ReadsPolicy)]
    public async Task<ActionResult<IReadOnlyDictionary<string, SnapshotDto>>> Snapshots([FromQuery] string keys = "", CancellationToken ct = default)
    {
        var parsed = new List<(MediaType, int)>();
        foreach (var raw in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':');
            if (parts.Length == 2 && Enum.TryParse<MediaType>(parts[0], true, out var type) && int.TryParse(parts[1], out var id))
                parsed.Add((type, id));
        }

        // A full home page is ~180 cards; one request should cover it.
        if (parsed.Count > 200)
            return BadRequest(new { error = "At most 200 keys per request." });

        return Ok(await _queries.GetSnapshotsAsync(parsed, ct));
    }
}
