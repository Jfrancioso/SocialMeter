using System.Text.Json;
using System.Text.RegularExpressions;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Tmdb.Dtos;

namespace SocialMeter.Infrastructure.Trailers;

/// <summary>
/// Picks the one trailer worth showing. Both providers' data ends up in an iframe or a video element on our pages, so
/// ids and URLs are validated here: a YouTube id must look like one, and a Steam stream must live on Steam's CDN.
/// </summary>
public static partial class TrailerPicker
{
    /// <summary>
    /// TMDB lists every clip, featurette and teaser, and contributors file promo spots ("The Plane") as trailers too. Prefer
    /// a real trailer over a teaser, one actually called a trailer, the studio's own upload, English, and the newest
    /// (usually the final trailer).
    /// </summary>
    public static TitleTrailer? FromTmdb(IEnumerable<TmdbVideo>? videos)
    {
        var best = (videos ?? Enumerable.Empty<TmdbVideo>())
            .Where(v => v.Site == "YouTube" && v.Key is not null && YouTubeId().IsMatch(v.Key) && v.Type is "Trailer" or "Teaser")
            .OrderByDescending(v => v.Type == "Trailer")
            .ThenByDescending(v => CalledTrailer(v.Name))
            .ThenByDescending(v => v.Official)
            .ThenByDescending(v => v.Language == "en")
            .ThenByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return best is null ? null : new TitleTrailer("youtube", string.IsNullOrWhiteSpace(best.Name) ? "Trailer" : best.Name!, YouTubeId: best.Key);
    }

    /// <summary>
    /// Steam's appdetails "movies": prefer the ones the store highlights and, among those, one called a trailer rather than a
    /// demo or gameplay reel; otherwise Steam's own order. Steam now serves only HLS and DASH streams; HLS plays natively in
    /// Safari and through a small player library elsewhere.
    /// </summary>
    public static TitleTrailer? FromSteam(JsonElement appDetails)
    {
        if (!appDetails.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array)
            return null;

        var ordered = movies.EnumerateArray()
            .Select((m, i) => (m, i))
            .OrderByDescending(x => Highlighted(x.m))
            .ThenByDescending(x => CalledTrailer(Str(x.m, "name")))
            .ThenBy(x => x.i)
            .Select(x => x.m);
        foreach (var m in ordered)
        {
            var stream = Str(m, "hls_h264");
            if (!IsSteamCdn(stream)) continue;

            var thumb = Str(m, "thumbnail");
            return new TitleTrailer("steam", Str(m, "name") is { Length: > 0 } n ? n : "Trailer",
                StreamUrl: stream, Thumbnail: IsSteamCdn(thumb) ? thumb : null);
        }
        return null;
    }

    private static bool CalledTrailer(string? name) => name?.Contains("trailer", StringComparison.OrdinalIgnoreCase) == true;

    private static bool Highlighted(JsonElement m) => m.TryGetProperty("highlight", out var h) && h.ValueKind == JsonValueKind.True;

    private static bool IsSteamCdn(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
           && u.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase);

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex YouTubeId();
}
