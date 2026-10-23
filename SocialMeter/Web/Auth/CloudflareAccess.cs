using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SocialMeter.Options;

namespace SocialMeter.Web.Auth;

/// <summary>
/// Validates the token Cloudflare Access attaches to each request it allows. The signing keys are fetched from the team
/// domain and cached (refreshed on their own and whenever a token names a key we have not seen). Nothing here trusts a
/// header without checking its signature.
/// </summary>
public static class CloudflareAccess
{
    public const string Scheme = "CloudflareAccess";
    public const string TokenHeader = "Cf-Access-Jwt-Assertion";
    public const string EmailClaim = "email";

    public static IServiceCollection AddCloudflareAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CloudflareAccessOptions>(configuration.GetSection(CloudflareAccessOptions.SectionName));

        services.AddAuthentication().AddJwtBearer(Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(Scheme).Configure<IOptions<CloudflareAccessOptions>>((jwt, access) =>
        {
            var options = access.Value;
            jwt.MapInboundClaims = false;   // keep "email" as "email"
            jwt.Events = new JwtBearerEvents
            {
                // Only Cloudflare's header counts; an Authorization header is never consulted.
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Headers[TokenHeader].FirstOrDefault();
                    if (string.IsNullOrEmpty(token)) ctx.NoResult();
                    else ctx.Token = token;
                    return Task.CompletedTask;
                }
            };
            if (!options.IsConfigured) return;   // the admin filter checks IsConfigured before ever authenticating

            jwt.TokenValidationParameters = ValidationParameters(options);
            jwt.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{options.Issuer}/cdn-cgi/access/certs", new KeysRetriever(options.Issuer), new HttpDocumentRetriever { RequireHttps = true });
        });
        return services;
    }

    /// <summary>What a valid Access token must satisfy. The keys come from the configuration manager.</summary>
    public static TokenValidationParameters ValidationParameters(CloudflareAccessOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience.Trim(),
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    /// <summary>Reads Cloudflare's certs document ({"keys": [...], "public_cert": ..., ...}) into the shape JwtBearer uses.</summary>
    public static OpenIdConnectConfiguration ParseCerts(string json, string issuer)
    {
        var config = new OpenIdConnectConfiguration { Issuer = issuer, JsonWebKeySet = new JsonWebKeySet(json) };
        foreach (var key in config.JsonWebKeySet.GetSigningKeys())
            config.SigningKeys.Add(key);
        return config;
    }

    private sealed class KeysRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private readonly string _issuer;
        public KeysRetriever(string issuer) => _issuer = issuer;

        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
            => ParseCerts(await retriever.GetDocumentAsync(address, cancel), _issuer);
    }
}
