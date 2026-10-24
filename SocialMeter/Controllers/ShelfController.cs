using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Application.Shelf;
using SocialMeter.Contracts.Shelf;
using SocialMeter.Web.Filters;

namespace SocialMeter.Controllers;

/// <summary>
/// The private reading shelf (/shelf on the site): read-only, for admins and the emails on Shelf:Readers. In production
/// Cloudflare Access stands in front of this path, as it does for /api/admin, and the API checks the token again. A
/// reader can use only these endpoints; the admin ones still refuse them (see <see cref="RequireAdminAttribute"/>).
/// </summary>
[ApiController]
[Route("api/shelf")]
[RequireAdmin(ReadersToo = true)]
[EnableCors(Web.DependencyInjection.AdminCorsPolicy)]
public sealed class ShelfController : ControllerBase
{
    public const string CoverClient = "shelf-covers";
    private static readonly TimeSpan CoverCacheFor = TimeSpan.FromDays(7);
    private const int MaxCoverBytes = 2_000_000;

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ShelfController> _logger;

    public ShelfController(IConfiguration configuration, IHttpClientFactory http, IMemoryCache cache, ILogger<ShelfController> logger)
    {
        _configuration = configuration;
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public IReadOnlyList<ShelfSeriesDto> Get() => ReadingShelf.Series;

    /// <summary>Sign-in for the shelf, as for the admin page: Access asks for the one-time code, then back to /shelf.</summary>
    [HttpGet("sign-in")]
    public IActionResult SignIn([FromQuery] string? returnTo) =>
        AdminController.SafeReturnOrigin(returnTo, Web.DependencyInjection.AllowedOrigins(_configuration), "/shelf") is { } origin
            ? Redirect($"{origin}/shelf")
            : BadRequest(new ProblemDetails { Status = 400, Title = "returnTo must be the site's /shelf page" });

    /// <summary>
    /// A shelf cover from Open Library, which serves covers through redirects to archive.org: fetching them here keeps the
    /// site's image policy to its own API instead of allowing archive.org. Only the shelf's own cover ids are fetched,
    /// and each is kept for a week after the first view.
    /// </summary>
    [HttpGet("covers/{coverId:int}")]
    public async Task<IActionResult> Cover(int coverId, CancellationToken ct)
    {
        if (!ReadingShelf.HasCover(coverId)) return NotFound();

        var key = $"shelf:cover:{coverId}";
        if (!_cache.TryGetValue(key, out byte[]? image))
        {
            image = await FetchCoverAsync(coverId, ct);
            if (image is null) return NotFound();
            _cache.Set(key, image, CoverCacheFor);
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(image!, "image/jpeg");
    }

    private async Task<byte[]?> FetchCoverAsync(int coverId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.CreateClient(CoverClient).GetAsync($"https://covers.openlibrary.org/b/id/{coverId}-M.jpg", ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/jpeg") return null;
            if (response.Content.Headers.ContentLength > MaxCoverBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length is > 0 and <= MaxCoverBytes ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Shelf: could not fetch cover {CoverId} ({Error})", coverId, ex.GetType().Name);
            return null;
        }
    }
}
