namespace SocialMeter.Options;

/// <summary>
/// Cloudflare Access in front of the admin endpoints. Cloudflare signs every request it lets through with a JWT in the
/// Cf-Access-Jwt-Assertion header; the API checks that token itself (signature, issuer, audience, expiry) so a request
/// that somehow skipped Cloudflare still fails. Both values come from the Zero Trust dashboard and are not secrets, but
/// live in App Service settings rather than appsettings because this repository is public.
/// </summary>
public sealed class CloudflareAccessOptions
{
    public const string SectionName = "CloudflareAccess";

    /// <summary>"https://&lt;team&gt;.cloudflareaccess.com", the token issuer; its keys are at /cdn-cgi/access/certs.</summary>
    public string TeamDomain { get; set; } = "";

    /// <summary>The Access application's Audience (AUD) tag.</summary>
    public string Audience { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TeamDomain) && !string.IsNullOrWhiteSpace(Audience);

    /// <summary>The issuer exactly as Cloudflare writes it: no trailing slash.</summary>
    public string Issuer => TeamDomain.Trim().TrimEnd('/');
}
