using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Options;

namespace SocialMeter.Application.Home;

/// <summary>
/// The titles with the most counted posts over the featured window (SocialMeter:FeaturedWindowDays), most first, up to
/// 30. One ranking, shared by the home hero, the search suggestions and "The crowd" (CrowdShapeService). The counts
/// change only when titles are pulled, so it is cached for 15 minutes.
/// </summary>
public sealed class MostTalkedAbout
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private const string CacheKey = "home:most-talked-about";

    private readonly IPostQueries _queries;
    private readonly SocialMeterOptions _options;
    private readonly IMemoryCache _cache;

    public MostTalkedAbout(IPostQueries queries, IOptions<SocialMeterOptions> options, IMemoryCache cache)
    {
        _queries = queries;
        _options = options.Value;
        _cache = cache;
    }

    /// <summary>The window the ranking counts over, in days (1-14).</summary>
    public int WindowDays => Math.Clamp(_options.FeaturedWindowDays, 1, 14);

    public async Task<IReadOnlyList<TalkedAbout>> GetAsync(CancellationToken ct = default) =>
        await _cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return _queries.GetMostTalkedAboutAsync(DateTime.UtcNow.AddDays(-WindowDays), Math.Max(1, _options.FeaturedMinPosts), 30, ct);
        }) ?? Array.Empty<TalkedAbout>();
}
