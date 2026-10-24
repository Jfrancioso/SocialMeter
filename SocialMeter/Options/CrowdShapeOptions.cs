namespace SocialMeter.Options;

/// <summary>
/// The Crowd Shape pilot (2026-10-24): which title pages show "The crowd", the dot field of counted posts. Off everywhere
/// else. To turn it off with no deploy: App Service setting CrowdShape__Enabled=false (the restart it causes is the only
/// step). To move it to another title: CrowdShape__Titles__0=tv_124364.
/// </summary>
public sealed class CrowdShapeOptions
{
    public const string SectionName = "CrowdShape";

    public bool Enabled { get; set; } = true;

    /// <summary>Title keys as "{type}_{id}", the same form as Search:Titles (e.g. "movie_1400837" for Other Mommy).</summary>
    public List<string> Titles { get; set; } = new();

    /// <summary>At most this many posts are drawn, the heaviest first; the shares and the shape always use every counted post.</summary>
    public int MaxDots { get; set; } = 300;

    public bool IsOn(string titleKey) => Enabled && Titles.Contains(titleKey, StringComparer.OrdinalIgnoreCase);
}
