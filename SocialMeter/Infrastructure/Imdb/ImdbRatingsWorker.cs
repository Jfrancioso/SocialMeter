using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Infrastructure.Imdb;

/// <summary>Downloads IMDb's ratings file at start-up and then once a day. A failed download keeps the previous table.</summary>
public sealed class ImdbRatingsWorker : BackgroundService
{
    private readonly ImdbRatingsDataset _dataset;
    private readonly IHttpClientFactory _http;
    private readonly RatingsOptions _options;
    private readonly ILogger<ImdbRatingsWorker> _logger;

    public ImdbRatingsWorker(ImdbRatingsDataset dataset, IHttpClientFactory http, IOptions<RatingsOptions> options, ILogger<ImdbRatingsWorker> logger)
    {
        _dataset = dataset;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ImdbDatasetUrl)) return;
        var interval = TimeSpan.FromHours(Math.Max(1, _options.ImdbRefreshHours));

        // Let the app finish starting before an 8 MB download.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = _http.CreateClient(nameof(ImdbRatingsWorker));
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
                await using var stream = await client.GetStreamAsync(_options.ImdbDatasetUrl, stoppingToken);
                await _dataset.LoadAsync(stream, stoppingToken);
                _logger.LogInformation("IMDb ratings loaded: {Count} titles", _dataset.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IMDb ratings download failed; keeping the previous table ({Count} titles)", _dataset.Count);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
