using System.Net;

namespace SocialMeter.Web;

/// <summary>
/// Who is calling, for per-visitor rate limits. Behind Cloudflare every request reaches Azure from a Cloudflare edge
/// address, so that address alone would make everyone at one Cloudflare data centre share one allowance. Cloudflare puts
/// the visitor's address in CF-Connecting-IP; it is trusted only when the connection itself came from Cloudflare's
/// published ranges, so a caller going straight to the Azure hostname cannot forge a fresh allowance with the header.
/// </summary>
public static class ClientAddress
{
    public const string CloudflareHeader = "CF-Connecting-IP";

    // https://www.cloudflare.com/ips-v4 and https://www.cloudflare.com/ips-v6 (checked 2026-10-22). Cloudflare changes these rarely.
    private static readonly IPNetwork[] CloudflareRanges =
    {
        IPNetwork.Parse("173.245.48.0/20"), IPNetwork.Parse("103.21.244.0/22"), IPNetwork.Parse("103.22.200.0/22"),
        IPNetwork.Parse("103.31.4.0/22"), IPNetwork.Parse("141.101.64.0/18"), IPNetwork.Parse("108.162.192.0/18"),
        IPNetwork.Parse("190.93.240.0/20"), IPNetwork.Parse("188.114.96.0/20"), IPNetwork.Parse("197.234.240.0/22"),
        IPNetwork.Parse("198.41.128.0/17"), IPNetwork.Parse("162.158.0.0/15"), IPNetwork.Parse("104.16.0.0/13"),
        IPNetwork.Parse("104.24.0.0/14"), IPNetwork.Parse("172.64.0.0/13"), IPNetwork.Parse("131.0.72.0/22"),
        IPNetwork.Parse("2400:cb00::/32"), IPNetwork.Parse("2606:4700::/32"), IPNetwork.Parse("2803:f800::/32"),
        IPNetwork.Parse("2405:b500::/32"), IPNetwork.Parse("2405:8100::/32"), IPNetwork.Parse("2a06:98c0::/29"),
        IPNetwork.Parse("2c0f:f248::/32")
    };

    /// <summary>The visitor's address as a stable key; "unknown" when there is none.</summary>
    public static string For(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null) return "unknown";
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();

        if (IsCloudflare(remote)
            && IPAddress.TryParse(ctx.Request.Headers[CloudflareHeader].FirstOrDefault(), out var visitor))
            return Key(visitor.IsIPv4MappedToIPv6 ? visitor.MapToIPv4() : visitor);

        return Key(remote);
    }

    /// <summary>
    /// The key for one visitor: the address for IPv4, the /64 network for IPv6. A home or phone line gets a whole IPv6 /64
    /// and can use any address in it, so a key per address would let one visitor walk past every per-visitor limit.
    /// </summary>
    public static string Key(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    public static bool IsCloudflare(IPAddress address) => CloudflareRanges.Any(r => r.Contains(address));
}
