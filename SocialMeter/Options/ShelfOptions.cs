namespace SocialMeter.Options;

/// <summary>
/// The private reading shelf (/shelf): who besides the admins may open it. Readers sign in through Cloudflare Access like
/// admins do, but this list only opens the shelf's read-only endpoints; every admin endpoint still refuses them.
/// </summary>
public sealed class ShelfOptions
{
    public const string SectionName = "Shelf";

    /// <summary>
    /// Reader emails (Shelf:Readers:0, ...). Kept in App Service settings, not appsettings, because the repository is
    /// public. Empty admits only admins.
    /// </summary>
    public string[] Readers { get; set; } = Array.Empty<string>();

    public bool IsReader(string? email) =>
        !string.IsNullOrWhiteSpace(email) && Readers.Any(r => string.Equals(r.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
