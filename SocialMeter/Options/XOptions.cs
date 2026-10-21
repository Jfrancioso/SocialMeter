namespace SocialMeter.Options;

/// <summary>X (Twitter) API v2. Needs an app bearer token from developer.x.com; the source is skipped without one.</summary>
public sealed class XOptions
{
    public const string SectionName = "X";

    public string BaseUrl { get; set; } = "https://api.x.com/2/";
    public string BearerToken { get; set; } = "";

    /// <summary>10–100 for the recent-search endpoint.</summary>
    public int MaxResults { get; set; } = 100;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BearerToken);
}
