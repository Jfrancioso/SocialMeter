namespace SocialMeter.Options;

/// <summary>Where the outside ratings come from beyond OMDb, which lags new releases and has no critics' scores for TV.</summary>
public sealed class RatingsOptions
{
    public const string SectionName = "Ratings";

    /// <summary>
    /// Read the score Rotten Tomatoes and Metacritic publish in their pages' structured data when OMDb has none.
    /// Their terms prohibit automated access; this is the switch to turn it off at once if either site objects.
    /// </summary>
    public bool ReadPublishedScores { get; set; } = true;

    /// <summary>How long a published score is reused before the page is read again.</summary>
    public int PublishedScoreCacheHours { get; set; } = 12;

    /// <summary>
    /// IMDb's own daily ratings file. Licensed for personal and non-commercial use only: revisit if the site earns money.
    /// Empty disables it and IMDb ratings come from OMDb alone.
    /// </summary>
    public string ImdbDatasetUrl { get; set; } = "https://datasets.imdbws.com/title.ratings.tsv.gz";

    /// <summary>IMDb refreshes the file daily.</summary>
    public int ImdbRefreshHours { get; set; } = 24;

    /// <summary>Honest identification for every outside page or file request.</summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; Chatterbub/1.0; +https://chatterbub.com)";
}
