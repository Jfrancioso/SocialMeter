namespace SocialMeter.Options;

/// <summary>
/// Reddit access through a "script" app (create one at reddit.com/prefs/apps). Reddit no longer serves its JSON
/// endpoints without OAuth, so the source is skipped until ClientId and ClientSecret are set.
/// </summary>
public sealed class RedditOptions
{
    public const string SectionName = "Reddit";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string UserAgent { get; set; } = "windows:socialmeter:v1.0 (by /u/socialmeter)";

    /// <summary>Max posts per search. Reddit caps this at 100.</summary>
    public int Limit { get; set; } = 100;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
