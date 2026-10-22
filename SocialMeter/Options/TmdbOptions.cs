namespace SocialMeter.Options;

public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3/";

    /// <summary>v3 API key, sent as a query parameter. Prefer <see cref="ReadAccessToken"/>, which travels in a header and never lands in a URL or a log.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>v4 "API Read Access Token" from the same TMDB settings page. When set it is used instead of the key.</summary>
    public string ReadAccessToken { get; set; } = "";

    public string Region { get; set; } = "US";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(ReadAccessToken);
    public bool UseBearer => !string.IsNullOrWhiteSpace(ReadAccessToken);
}
