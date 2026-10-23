using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SocialMeter.Controllers;
using SocialMeter.Options;
using SocialMeter.Web.Auth;
using SocialMeter.Web.Filters;
using Xunit;

namespace SocialMeter.Tests;

public class AdminAccessTests
{
    private static readonly CloudflareAccessOptions Access = new() { TeamDomain = "https://chatterbub.cloudflareaccess.com/", Audience = "aud-tag-123" };

    // ---- Cloudflare Access tokens ----

    private static (RsaSecurityKey Key, string CertsJson) KeyPair()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "kid-1" };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        // The shape Cloudflare serves at /cdn-cgi/access/certs, extra fields included.
        var certs = System.Text.Json.JsonSerializer.Serialize(new
        {
            keys = new[] { new { kid = jwk.Kid, kty = "RSA", alg = "RS256", use = "sig", e = jwk.E, n = jwk.N } },
            public_cert = new { kid = jwk.Kid, cert = "unused" }
        });
        return (key, certs);
    }

    private static string Token(RsaSecurityKey key, string issuer = "https://chatterbub.cloudflareaccess.com", string audience = "aud-tag-123",
        DateTime? expires = null, string email = "joey@example.com") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            NotBefore = (expires ?? DateTime.UtcNow.AddHours(1)).AddHours(-2),
            IssuedAt = (expires ?? DateTime.UtcNow.AddHours(1)).AddHours(-2),
            Claims = new Dictionary<string, object> { ["email"] = email },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });

    private static async Task<TokenValidationResult> ValidateAsync(string token, string certsJson)
    {
        var parameters = CloudflareAccess.ValidationParameters(Access);
        parameters.IssuerSigningKeys = CloudflareAccess.ParseCerts(certsJson, Access.Issuer).SigningKeys;
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
    }

    [Fact]
    public async Task A_token_signed_by_the_team_for_this_app_is_accepted()
    {
        var (key, certs) = KeyPair();
        var result = await ValidateAsync(Token(key), certs);
        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("joey@example.com", result.Claims["email"]);
    }

    [Fact]
    public async Task Tokens_for_another_app_team_or_key_or_out_of_date_are_refused()
    {
        var (key, certs) = KeyPair();
        var (otherKey, _) = KeyPair();
        Assert.False((await ValidateAsync(Token(key, audience: "someone-elses-app"), certs)).IsValid);
        Assert.False((await ValidateAsync(Token(key, issuer: "https://evil.cloudflareaccess.com"), certs)).IsValid);
        Assert.False((await ValidateAsync(Token(otherKey), certs)).IsValid);
        Assert.False((await ValidateAsync(Token(key, expires: DateTime.UtcNow.AddMinutes(-5)), certs)).IsValid);
    }

    [Fact]
    public void The_issuer_has_no_trailing_slash_and_needs_both_values()
    {
        Assert.Equal("https://chatterbub.cloudflareaccess.com", Access.Issuer);
        Assert.False(new CloudflareAccessOptions { TeamDomain = "https://x.cloudflareaccess.com" }.IsConfigured);
    }

    // ---- Who counts as an admin ----

    [Fact]
    public void Only_listed_emails_are_admins_and_an_empty_list_admits_nobody()
    {
        var admin = new AdminOptions { Emails = new[] { "Joey@Example.com " } };
        Assert.True(AdminKey.IsAdminEmail("joey@example.com", admin));
        Assert.False(AdminKey.IsAdminEmail("someone@example.com", admin));
        Assert.False(AdminKey.IsAdminEmail(null, admin));
        Assert.False(AdminKey.IsAdminEmail("joey@example.com", new AdminOptions()));
    }

    [Fact]
    public void The_admin_key_must_match_exactly_and_must_be_configured()
    {
        Assert.True(AdminKey.Matches("s3cret", new AdminOptions { ApiKey = "s3cret" }));
        Assert.False(AdminKey.Matches("s3cre", new AdminOptions { ApiKey = "s3cret" }));
        Assert.False(AdminKey.Matches(null, new AdminOptions { ApiKey = "s3cret" }));
        Assert.False(AdminKey.Matches("", new AdminOptions()));
    }

    // ---- The sign-in bounce only ever returns to the site's admin page ----

    [Theory]
    [InlineData("https://chatterbub.com/admin", true)]
    [InlineData("https://chatterbub.com/admin/log", true)]
    [InlineData("http://localhost:5173/admin", true)]
    [InlineData("https://chatterbub.com/", false)]
    [InlineData("https://chatterbub.com/administrator", false)]
    [InlineData("https://chatterbub.com.evil.com/admin", false)]
    [InlineData("https://evil.com/admin", false)]
    [InlineData("http://chatterbub.com/admin", false)]
    [InlineData("https://user@chatterbub.com/admin", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/admin", false)]
    [InlineData(null, false)]
    public void Sign_in_returns_only_to_the_admin_page(string? returnTo, bool expected)
    {
        var origins = new[] { "https://chatterbub.com", "https://www.chatterbub.com", "http://localhost:5173" };
        Assert.Equal(expected, AdminController.IsSafeReturn(returnTo, origins));
    }
}
