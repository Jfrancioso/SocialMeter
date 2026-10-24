using System.Net;
using Microsoft.AspNetCore.Http;
using SocialMeter.Web;
using Xunit;

namespace SocialMeter.Tests;

public class ClientAddressTests
{
    private static HttpContext Request(string remote, string? cfHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (cfHeader is not null) ctx.Request.Headers[ClientAddress.CloudflareHeader] = cfHeader;
        return ctx;
    }

    [Fact]
    public void Behind_cloudflare_the_visitor_header_is_the_client()
        => Assert.Equal("203.0.113.7", ClientAddress.For(Request("172.70.1.2", "203.0.113.7")));

    [Fact]
    public void A_forged_header_from_outside_cloudflare_is_ignored()
        => Assert.Equal("198.51.100.9", ClientAddress.For(Request("198.51.100.9", "203.0.113.7")));

    [Fact]
    public void Without_the_header_the_connection_address_is_the_client()
        => Assert.Equal("172.70.1.2", ClientAddress.For(Request("172.70.1.2")));

    [Fact]
    public void Ipv6_cloudflare_edges_are_recognised()
        => Assert.Equal("2001:db8::/64", ClientAddress.For(Request("2606:4700::1", "2001:db8::5")));

    [Fact]
    public void An_ipv6_visitor_is_one_key_for_their_whole_64()
    {
        // A home or phone line can use any address in its /64, so per-visitor limits must count the /64 once.
        Assert.Equal(ClientAddress.For(Request("2606:4700::1", "2001:db8:1:2:aaaa::1")), ClientAddress.For(Request("2606:4700::1", "2001:db8:1:2:ffff:1:2:3")));
        Assert.NotEqual(ClientAddress.For(Request("2606:4700::1", "2001:db8:1:2::1")), ClientAddress.For(Request("2606:4700::1", "2001:db8:1:3::1")));
    }

    [Fact]
    public void Ipv4_mapped_addresses_are_normalised()
        => Assert.Equal("203.0.113.7", ClientAddress.For(Request("::ffff:172.70.1.2", "203.0.113.7")));

    [Fact]
    public void Garbage_in_the_header_falls_back_to_the_connection()
        => Assert.Equal("172.70.1.2", ClientAddress.For(Request("172.70.1.2", "not-an-ip")));
}
