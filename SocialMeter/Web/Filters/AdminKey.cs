using System.Security.Cryptography;
using System.Text;
using SocialMeter.Options;

namespace SocialMeter.Web.Filters;

/// <summary>The checks both admin filters share.</summary>
public static class AdminKey
{
    /// <summary>Constant-time comparison so the key cannot be guessed byte by byte. False when no key is configured.</summary>
    public static bool Matches(string? presented, AdminOptions options) =>
        options.IsConfigured && presented is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(options.ApiKey));

    /// <summary>Case-insensitive match against Admin:Emails. An empty list admits nobody.</summary>
    public static bool IsAdminEmail(string? email, AdminOptions options) =>
        !string.IsNullOrWhiteSpace(email) && options.Emails.Any(e => string.Equals(e.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
