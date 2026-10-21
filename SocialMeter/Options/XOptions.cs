namespace SocialMeter.Options;

/// <summary>X (Twitter) API v2. Needs an app bearer token from developer.x.com; the source is skipped without one.</summary>
public sealed class XOptions
{
    public const string SectionName = "X";

    public string BaseUrl { get; set; } = "https://api.x.com/2/";
    public string BearerToken { get; set; } = "";

    /// <summary>10–100 for the recent-search endpoint.</summary>
    public int MaxResults { get; set; } = 100;

    /// <summary>
    /// Phrases that mark promo, news and listings rather than reactions; posts containing them are not fetched, so they
    /// cost nothing. Measured against ~700 real posts: every one of these was off-topic in 90–100 % of matches, while
    /// link, media and hashtag filters would have thrown away a third of the genuine reactions. Keep this list to
    /// phrases a person reacting to a title would almost never write.
    /// </summary>
    public List<string> ExcludeTerms { get; set; } = new()
    {
        "box office", "tickets", "presale", "showtimes", "now streaming", "now available", "premiere", "poster",
        "interview", "release date", "official", "giveaway"
    };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BearerToken);
}
