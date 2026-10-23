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
    private readonly HomeOptions _home;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ITitleService titles, ITitleTracker tracker, IPostQueries queries, IOptions<SocialMeterOptions> options,
        IOptions<HomeOptions> home, IMemoryCache cache, ILogger<HomeController> logger)
    {
        _titles = titles;
        _tracker = tracker;
        _queries = queries;
        _options = options.Value;
        _home = home.Value;
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

        // Trending is always fetched: the hero falls back to it. Every other row is fetched only if Home:Rows lists it.
        var wanted = _home.Effective;
        var trendingTask = _titles.GetTrendingAsync(null, 1, ct);
        var builders = new Dictionary<string, Func<Task<IReadOnlyList<HomeRowDto>>>>
        {
            ["trending"] = async () => Row("trending", "Trending now", "What the internet is talking about today", (await trendingTask).Items),
            ["now_playing"] = async () => Row("now_playing", "In theaters", "Playing near you", (await _titles.GetNowPlayingAsync(region, 1, ct)).Items),
            ["seasonal"] = async () =>
            {
                var (label, subtitle, items) = await _tracker.GetSeasonalRowAsync(ct);
                return Row("seasonal", label, subtitle, items);
            },
            ["on_the_air"] = async () => Row("on_the_air", "On TV this week", "Shows with new episodes airing", (await _titles.GetOnTheAirAsync(1, ct)).Items),
            ["top_games"] = async () => Row("top_games", "Hot on Steam", "This week's top sellers", await SafeAsync(_titles.GetTrendingAsync(MediaType.Game, 1, ct))),
            ["popular_movies"] = async () => Row("popular_movies", "Popular movies", null, (await _titles.GetPopularAsync(MediaType.Movie, 1, ct)).Items),
            ["popular_tv"] = async () => Row("popular_tv", "Popular shows", null, (await _titles.GetPopularAsync(MediaType.Tv, 1, ct)).Items),
            ["most_played"] = async () => Row("most_played", "Most played right now", "Steam's live player counts", await SafeAsync(_titles.GetPopularAsync(MediaType.Game, 1, ct)))
        };
        // October's horror pair comes from one call; it supplies whichever of its two rows are listed.
        if (wanted.Contains("horror_trending") || wanted.Contains("horror_theaters"))
            builders["horror"] = async () => (await _tracker.GetSeasonalTopRowsAsync(region, ct))
                .Select(r => new HomeRowDto { Key = r.Key, Title = r.Title, Subtitle = r.Subtitle, Items = r.Items }).ToList();

        var fetched = await Task.WhenAll(builders
            .Where(b => wanted.Contains(b.Key) || b.Key == "horror")
            .Select(b => b.Value()));
        var byKey = fetched.SelectMany(rs => rs).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());

        var trending = await trendingTask;
        var (featured, featuredLabel, featuredPosts) = await PickFeaturedAsync(trending.Items, ct);
        var rows = Dedupe(wanted.Where(byKey.ContainsKey).Select(k => byKey[k]));

        await _tracker.AnnotateAsync(rows.SelectMany(r => r.Items).Concat(featured is null ? [] : [featured]), ct);

        var response = new HomeResponseDto
        {
            Featured = featured,
            FeaturedLabel = featuredLabel,
            FeaturedPosts = featuredPosts,
            FeaturedWindowDays = _options.FeaturedWindowDays,
            Rows = rows
        };
        _cache.Set(key, response, CacheFor);
        return Ok(response);
    }

    /// <summary>
    /// What the search box offers before anything is typed: the titles people are posting about most this week (from our
    /// own counts, games included) and TMDB's trending list. Built from data already on hand; nothing about the visitor
    /// or what they search for is recorded.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<ActionResult<SearchSuggestionsDto>> Suggestions(CancellationToken ct)
    {
        var result = await _cache.GetOrCreateAsync("home:suggestions", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FeaturedCacheFor;
            var talked = new List<TalkedAboutDto>();
            var windowDays = Math.Clamp(_options.FeaturedWindowDays, 1, 14);
            try
            {
                var since = DateTime.UtcNow.AddDays(-windowDays);
                var candidates = await _queries.GetMostTalkedAboutAsync(since, Math.Max(1, _options.FeaturedMinPosts), 30, ct);
                // Steam reviews arrive by the hundred and would crowd out every movie and show, so games get one place.
                var picks = candidates.Where(c => c.MediaType != MediaType.Game).Take(4)
                    .Concat(candidates.Where(c => c.MediaType == MediaType.Game).Take(1))
                    .OrderByDescending(c => c.Posts);
                foreach (var c in picks)
                    if (await _titles.GetDetailsAsync(c.MediaType, c.TmdbId, ct) is { } title)
                        talked.Add(new TalkedAboutDto { Title = title, Posts = c.Posts });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Search suggestions: most-talked-about lookup failed; offering trending only");
            }

            var trending = await _titles.GetTrendingAsync(null, 1, ct);
            var seen = talked.Select(t => (t.Title.MediaType, t.Title.TmdbId)).ToHashSet();
            return new SearchSuggestionsDto
            {
                TalkedAbout = talked,
                Trending = trending.Items.Where(t => !seen.Contains((t.MediaType, t.TmdbId))).Take(5).ToList(),
                WindowDays = windowDays
            };
        });
        return Ok(result);
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

    private static IReadOnlyList<HomeRowDto> Row(string key, string title, string? subtitle, IReadOnlyList<Title> items) =>
        new[] { new HomeRowDto { Key = key, Title = title, Subtitle = subtitle, Items = items } };

    /// <summary>
    /// Each title appears only in the first row (in page order) that has it, so the page shows more titles and fewer
    /// repeats; a row left with nothing is dropped.
    /// </summary>
    public static List<HomeRowDto> Dedupe(IEnumerable<HomeRowDto> rows)
    {
        var seen = new HashSet<(MediaType, int)>();
        var result = new List<HomeRowDto>();
        foreach (var row in rows)
        {
            var items = row.Items.Where(t => seen.Add((t.MediaType, t.TmdbId))).ToList();
            if (items.Count > 0)
                result.Add(new HomeRowDto { Key = row.Key, Title = row.Title, Subtitle = row.Subtitle, Items = items });
        }
        return result;
    }

    /// <summary>Steam is a second provider; if it is down the home page should still render the TMDB rows.</summary>
    private static async Task<IReadOnlyList<Title>> SafeAsync(Task<PagedResult<Title>> pending)
    {
        try { return (await pending).Items; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Array.Empty<Title>(); }
    }
}
