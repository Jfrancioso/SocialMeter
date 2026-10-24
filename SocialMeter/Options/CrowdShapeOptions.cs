namespace SocialMeter.Options;

/// <summary>
/// "The crowd" (Crowd Shape, since 2026-10-24): which title pages show every counted post as a dot. A title gets it when
/// it is listed here, when a watch window is open for it (a live event), or when it is among the TopTalkedAbout titles of
/// the home page's "Most talked about" ranking and has at least MinPosts counted posts. Everything else keeps the page it had.
/// Off everywhere, no deploy: App Service setting CrowdShape__Enabled=false (the restart it causes is the only step).
/// </summary>
public sealed class CrowdShapeOptions
{
    public const string SectionName = "CrowdShape";

    public bool Enabled { get; set; } = true;

    /// <summary>Title keys as "{type}_{id}", the same form as Search:Titles (e.g. "movie_1400837" for Other Mommy). Always shown.</summary>
    public List<string> Titles { get; set; } = new();

    /// <summary>How many titles from the top of the "Most talked about" ranking get the crowd; 0 = none.</summary>
    public int TopTalkedAbout { get; set; } = 10;

    /// <summary>A most-talked-about title needs this many counted posts; fewer dots don't show a shape. Listed and live titles always show.</summary>
    public int MinPosts { get; set; } = 20;

    /// <summary>At most this many posts are drawn, the heaviest first; the shares and the shape always use every counted post.</summary>
    public int MaxDots { get; set; } = 200;

    public bool IsListed(string titleKey) => Titles.Contains(titleKey, StringComparer.OrdinalIgnoreCase);
}
