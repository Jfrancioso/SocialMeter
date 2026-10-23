using Microsoft.Extensions.Configuration;
using SocialMeter.Application.Home;
using SocialMeter.Contracts.Home;
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
        var rows = HomeLayout.Dedupe(new[]
        {
            R("horror", T(1), T(2)),
            R("trending", T(2), T(3), T(1, MediaType.Tv)),   // same id, different type: a different title
            R("theaters", T(1), T(3))                         // nothing new: the row disappears
        });

        Assert.Equal(new[] { "horror", "trending" }, rows.Select(r => r.Key));
        Assert.Equal(new[] { 3, 1 }, rows[1].Items.Select(t => t.TmdbId));
    }

    [Fact]
    public void Signal_rows_keep_titles_that_list_rows_already_showed()
    {
        var rows = HomeLayout.Dedupe(new[]
        {
            R("horror_trending", T(1), T(2)),
            R("gaps", T(1)),                  // Clayface is in the horror row and still belongs in "Crowd vs critics"
            R("trending", T(1), T(3))         // the list row still loses the repeat
        });

        Assert.Equal(new[] { 1 }, rows.Single(r => r.Key == "gaps").Items.Select(t => t.TmdbId));
        Assert.Equal(new[] { 3 }, rows.Single(r => r.Key == "trending").Items.Select(t => t.TmdbId));
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

    [Fact]
    public void Search_suggestions_keep_one_place_for_games()
    {
        var picks = SearchSuggestions.Pick(new[]
        {
            new TalkedAbout(MediaType.Game, 1, 900, 1), new TalkedAbout(MediaType.Game, 2, 800, 1),   // Steam reviews arrive by the hundred
            new TalkedAbout(MediaType.Movie, 3, 60, 1), new TalkedAbout(MediaType.Tv, 4, 50, 1),
            new TalkedAbout(MediaType.Movie, 5, 40, 1), new TalkedAbout(MediaType.Movie, 6, 30, 1), new TalkedAbout(MediaType.Movie, 7, 20, 1)
        });

        Assert.Equal(new[] { 1, 3, 4, 5, 6 }, picks.Select(p => p.TmdbId));   // busiest first: one game, four movies and shows
    }
}
