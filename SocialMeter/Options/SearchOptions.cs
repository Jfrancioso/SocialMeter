namespace SocialMeter.Options;

/// <summary>
/// Per-title tuning of the social searches, for titles whose names are everyday words (see TitleSearch). Keyed by
/// "{type}_{id}", e.g. "tv_124364" for FROM. As App Service settings: Search__Titles__tv_124364__Terms__0 = Fromville.
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    // Case-insensitive so "TV_124364" and "tv_124364" are the same title.
    public Dictionary<string, TitleSearchTuning> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TitleSearchTuning
{
    /// <summary>Extra words or phrases that mark a post as being about this title: a place, a nickname, a hashtag.</summary>
    public List<string> Terms { get; set; } = new();

    /// <summary>Force the title to be treated as ambiguous (true) or distinctive (false), whatever its name looks like.</summary>
    public bool? Ambiguous { get; set; }
}
