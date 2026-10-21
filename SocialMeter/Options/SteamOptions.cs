namespace SocialMeter.Options;

/// <summary>
/// The store endpoints SocialMeter relies on need no key. A Steam Web API key (steamcommunity.com/dev/apikey)
/// additionally unlocks IStoreService/GetItems, which returns the real library artwork (portrait capsule + hero)
/// for every game; without it, new games fall back to the landscape header and page background.
/// </summary>
public sealed class SteamOptions
{
    public const string SectionName = "Steam";

    public string ApiKey { get; set; } = "";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
