namespace SocialMeter.Options;

/// <summary>OMDb supplies IMDb, Rotten Tomatoes and Metacritic ratings. Free keys (1,000 req/day) at omdbapi.com.</summary>
public sealed class OmdbOptions
{
    public const string SectionName = "Omdb";

    public string BaseUrl { get; set; } = "https://www.omdbapi.com/";
    public string ApiKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
