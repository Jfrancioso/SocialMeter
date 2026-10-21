namespace SocialMeter.Domain.Models;

/// <summary>An image attached to a post. Videos and GIFs are represented by their preview frame.</summary>
/// <param name="Url">Image URL on the platform's CDN.</param>
/// <param name="Type">"photo", "video" or "animated_gif".</param>
/// <param name="Alt">Author-supplied alt text, when any.</param>
public sealed record PostMedia(string Url, string Type, string? Alt);
