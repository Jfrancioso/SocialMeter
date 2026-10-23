using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Home;

/// <summary>
/// Rebuilds the cached home page and search suggestions every Home:WarmEveryMinutes, before the cached copies expire.
/// Building the home page takes about 3 seconds (a dozen TMDB lists plus our own signal rows); without this, the first
/// visitor after each expiry waited for it, which was most of the home page's slow loads in Cloudflare's Web Analytics.
/// Only the "US" page is warmed: it is the one the site asks for.
/// </summary>
public sealed class HomeWarmupWorker : BackgroundService
{
    private const string Region = "US";

    private readonly IServiceScopeFactory _scopes;
    private readonly HomeOptions _options;
    private readonly ILogger<HomeWarmupWorker> _logger;

    public HomeWarmupWorker(IServiceScopeFactory scopes, IOptions<HomeOptions> options, ILogger<HomeWarmupWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.WarmEveryMinutes <= 0) return;
        var interval = TimeSpan.FromMinutes(_options.WarmEveryMinutes);

        // Let start-up (migrations, the tracked set, the IMDb file) settle first.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A new scope each time: a fresh DbContext for the build's database reads.
                using var scope = _scopes.CreateScope();
                var feed = scope.ServiceProvider.GetRequiredService<HomeFeedService>();
                await feed.BuildHomeAsync(Region, stoppingToken);
                await feed.BuildSuggestionsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Visitors still get a page: they build it themselves, as before. Not an error worth an alert on its own.
                _logger.LogWarning(ex, "Home warm-up failed; the next visitor builds the page instead");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
