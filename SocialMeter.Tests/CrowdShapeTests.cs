using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

/// <summary>The Crowd Shape pilot: the shape rules, the one-title switch, and reading a clicked dot's post.</summary>
public class CrowdShapeTests
{
    [Theory]
    [InlineData(45, 43, 12, 43, CrowdShapeRules.Split)]              // Other Mommy, 2026-10-23: "49 Lukewarm" on the gauge
    [InlineData(56, 35, 9, 58, CrowdShapeRules.Split)]               // Carrie
    [InlineData(81, 14, 5, 152, CrowdShapeRules.Loved)]              // Clayface
    [InlineData(27, 69, 4, 28, CrowdShapeRules.Panned)]              // Scream 7
    [InlineData(30, 25, 45, 40, CrowdShapeRules.Mixed)]
    [InlineData(62, 28, 10, 40, CrowdShapeRules.LeaningPositive)]
    [InlineData(25, 50, 25, 40, CrowdShapeRules.LeaningNegative)]
    [InlineData(73, 27, 0, 9, CrowdShapeRules.Early)]                // FROM: 9 posts is too few to call
    public void Shapes_follow_the_shares(double pos, double neg, double neu, int posts, string expected)
        => Assert.Equal(expected, CrowdShapeRules.Shape(pos, neg, neu, posts));

    [Fact]
    public void Split_wins_over_loved_and_panned_when_both_sides_are_big()
        => Assert.Equal(CrowdShapeRules.Split, CrowdShapeRules.Shape(30, 60, 10, 50));

    [Fact]
    public void The_switch_matches_title_keys_and_can_be_turned_off()
    {
        var options = new CrowdShapeOptions { Titles = { "movie_1400837" } };
        Assert.True(options.IsOn("movie_1400837"));
        Assert.True(options.IsOn("MOVIE_1400837"));
        Assert.False(options.IsOn("tv_124364"));

        options.Enabled = false;
        Assert.False(options.IsOn("movie_1400837"));
    }

    private static SocialMeterDbContext Db() =>
        new(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SocialPost Post(int tmdbId, Sentiment sentiment, double tone, int upvotes = 5, bool excluded = false, string platform = "bluesky") => new()
    {
        TmdbId = tmdbId,
        MediaType = MediaType.Movie,
        Platform = platform,
        ExternalId = Guid.NewGuid().ToString(),
        Author = "someone",
        Content = "A post about the movie.",
        Sentiment = sentiment,
        SentimentScore = tone,
        TrustWeight = 1,
        Substance = 0.6,
        Upvotes = upvotes,
        Excluded = excluded,
        CreatedAt = DateTime.UtcNow.AddDays(-1)
    };

    private static CrowdShapeService Service(SocialMeterDbContext db, CrowdShapeOptions options) =>
        new(db, Microsoft.Extensions.Options.Options.Create(options));

    [Fact]
    public async Task A_title_outside_the_pilot_gets_nothing()
    {
        var db = Db();
        db.SocialPosts.Add(Post(2, Sentiment.Positive, 0.8));
        await db.SaveChangesAsync();

        var result = await Service(db, new CrowdShapeOptions { Titles = { "movie_1" } }).GetAsync(MediaType.Movie, 2);

        Assert.False(result.Enabled);
        Assert.Empty(result.Dots);
    }

    [Fact]
    public async Task A_pilot_title_gets_every_counted_post_as_a_dot_and_its_shape()
    {
        var db = Db();
        for (var i = 0; i < 6; i++) db.SocialPosts.Add(Post(1, Sentiment.Positive, 0.8, platform: i % 2 == 0 ? "x" : "bluesky"));
        for (var i = 0; i < 5; i++) db.SocialPosts.Add(Post(1, Sentiment.Negative, -0.6));
        db.SocialPosts.Add(Post(1, Sentiment.Positive, 0.9, upvotes: 5000, excluded: true));   // filtered: never a dot
        db.SocialPosts.Add(Post(2, Sentiment.Negative, -0.9));                                  // another title
        await db.SaveChangesAsync();

        var result = await Service(db, new CrowdShapeOptions { Titles = { "movie_1" } }).GetAsync(MediaType.Movie, 1);

        Assert.True(result.Enabled);
        Assert.Equal(11, result.Posts);
        Assert.Equal(11, result.Dots.Count);
        Assert.Equal(CrowdShapeRules.Split, result.Shape);
        Assert.Equal(100, result.Positive + result.Negative + result.Neutral, 0);
        Assert.All(result.Dots.Where(d => d.Sentiment == "positive"), d => Assert.Equal(0.9, d.Value, 3));   // 0.5 x 0.8 + 0.5
        Assert.All(result.Dots.Where(d => d.Sentiment == "negative"), d => Assert.Equal(-0.8, d.Value, 3));  // 0.5 x -0.6 - 0.5
        Assert.Contains(result.Dots, d => d.Platform == "x");
    }

    [Fact]
    public async Task Only_the_heaviest_posts_are_drawn_when_there_are_too_many_but_the_shares_use_all()
    {
        var db = Db();
        db.SocialPosts.Add(Post(1, Sentiment.Negative, -0.8, upvotes: 900));
        for (var i = 0; i < 12; i++) db.SocialPosts.Add(Post(1, Sentiment.Positive, 0.8, upvotes: 1));
        await db.SaveChangesAsync();

        var result = await Service(db, new CrowdShapeOptions { Titles = { "movie_1" }, MaxDots = 3 }).GetAsync(MediaType.Movie, 1);

        Assert.Equal(13, result.Posts);
        Assert.Equal(3, result.Dots.Count);
        Assert.Equal("negative", result.Dots[0].Sentiment);   // the heaviest first
    }

    [Fact]
    public async Task A_clicked_dot_reads_only_a_counted_post_of_that_title()
    {
        var db = Db();
        var counted = Post(1, Sentiment.Positive, 0.8);
        var hidden = Post(1, Sentiment.Positive, 0.8, excluded: true);
        var other = Post(2, Sentiment.Positive, 0.8);
        db.SocialPosts.AddRange(counted, hidden, other);
        await db.SaveChangesAsync();

        var queries = new PostQueryService(db, new ScoreHistoryService(db, new PullGate(), Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions())));

        var found = await queries.GetPostAsync(MediaType.Movie, 1, counted.Id);
        Assert.Equal(counted.Id, found?.Id);
        Assert.Equal(100, found?.WeightShare);   // the only counted post carries all the weight
        Assert.Null(await queries.GetPostAsync(MediaType.Movie, 1, hidden.Id));
        Assert.Null(await queries.GetPostAsync(MediaType.Movie, 1, other.Id));
    }
}
