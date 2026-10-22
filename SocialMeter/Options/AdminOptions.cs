namespace SocialMeter.Options;

/// <summary>
/// Shared secret for the operator endpoints (rescore, deletion sync, tracked refresh), sent as the X-Admin-Key header.
/// Lives in user secrets / environment, never in appsettings. When it is not set those endpoints are disabled outright:
/// an open endpoint that re-sends every stored post to the model is an open invitation to run up the bill.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public const string HeaderName = "X-Admin-Key";

    public string ApiKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
