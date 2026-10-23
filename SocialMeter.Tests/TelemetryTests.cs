using SocialMeter.Web;
using Xunit;

namespace SocialMeter.Tests;

public class TelemetryTests
{
    [Fact]
    public void Outgoing_urls_keep_parameter_names_but_never_values()
    {
        var url = Telemetry.RedactUrl(new Uri("https://api.themoviedb.org/3/trending/all/day?api_key=SECRET123&page=1"));

        Assert.Equal("https://api.themoviedb.org/3/trending/all/day?api_key=Redacted&page=Redacted", url);
        Assert.DoesNotContain("SECRET123", url);
    }

    [Fact]
    public void Search_terms_on_our_own_requests_are_blanked()
    {
        Assert.Equal("query=Redacted&page=Redacted", Telemetry.RedactQuery("?query=my+private+search&page=1"));
        Assert.Equal("", Telemetry.RedactQuery(""));
        Assert.Equal("flag=Redacted", Telemetry.RedactQuery("?flag"));
    }

    [Fact]
    public void Urls_without_a_query_and_user_info_are_handled()
    {
        Assert.Equal("https://www.omdbapi.com/", Telemetry.RedactUrl(new Uri("https://www.omdbapi.com/")));
        Assert.Equal("https://example.com/a?k=Redacted", Telemetry.RedactUrl(new Uri("https://user:pass@example.com/a?k=v#frag")));
    }
}
