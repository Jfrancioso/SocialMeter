namespace SocialMeter.Domain.Models;

/// <summary>
/// A title's trailer. Movies and shows: <paramref name="Provider"/> "youtube" with the 11-character YouTube video id.
/// Games: "steam" with an HLS playlist on Steam's video CDN and a poster frame. Nothing plays until the reader asks.
/// </summary>
public sealed record TitleTrailer(string Provider, string Name, string? YouTubeId = null, string? StreamUrl = null, string? Thumbnail = null);
