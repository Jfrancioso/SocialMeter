using Microsoft.Extensions.Configuration;
using SocialMeter.Contracts.Home;
using SocialMeter.Controllers;
using SocialMeter.Domain.Models;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

public class HomeLayoutTests
{
    private static Title T(int id, MediaType type = MediaType.Movie) => new() { TmdbId = id, MediaType = type, Name = $"t{id}" };
    private static HomeRowDto R(string key, params Title[] items) => new() { Key = key, Title = key, Items = items };

    [Fact]
    public void A_title_shows_only_in_the_first_row_that_has_it()
    {
        var rows = HomeController.Dedupe(new[]
        {
            R("horror", T(1), T(2)),
            R("trending", T(2), T(3), T(1, MediaType.Tv)),   // same id, different type: a different title
            R("theaters", T(1), T(3))                         // nothing new: the row disappears
        });

        Assert.Equal(new[] { "horror", "trending" }, rows.Select(r => r.Key));
        Assert.Equal(new[] { 3, 1 }, rows[1].Items.Select(t => t.TmdbId));
    }

    [Fact]
    public void Configuration_can_shorten_and_reorder_the_rows()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Home:Rows:0"] = "most_played",
            ["Home:Rows:1"] = "trending"
        }).Build();
        var options = config.GetSection(HomeOptions.SectionName).Get<HomeOptions>()!;

        Assert.Equal(new[] { "most_played", "trending" }, options.Effective);
    }

    [Fact]
    public void Without_configuration_the_default_rows_apply()
    {
        Assert.Equal(HomeOptions.DefaultRows, new HomeOptions().Effective);
    }
}
