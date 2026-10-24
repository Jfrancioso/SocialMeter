using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SocialMeter.Application.Home;
using SocialMeter.Application.Social;
using SocialMeter.Application.Tracking;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

/// <summary>
/// "The crowd": the shape rules, which titles show it, the per-reading cache, and the post queries around it (a clicked
/// dot's post, the post list's order).
/// </summary>
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
    public void Listed_and_live_titles_always_show_talked_about_ones_need_enough_posts()
    {
        Assert.True(CrowdShapeRules.Shows(listed: true, liveEvent: false, talkedAbout: false, posts: 3, minPosts: 20));
        Assert.True(CrowdShapeRules.Shows(listed: false, liveEvent: true, talkedAbout: false, posts: 3, minPosts: 20));
        Assert.True(CrowdShapeRules.Shows(listed: false, liveEvent: false, talkedAbout: true, posts: 20, minPosts: 20));
        Assert.False(CrowdShapeRules.Shows(listed: false, liveEvent: false, talkedAbout: true, posts: 19, minPosts: 20));
        Assert.False(CrowdShapeRules.Shows(listed: false, liveEvent: false, talkedAbout: false, posts: 500, minPosts: 20));
    }

    [Fact]
    public void Dots_are_shared_out_by_weight_not_taken_heaviest_first()
    {
        // WARDOGS on 2026-10-24: 113 loved posts held 22% of the weight, but the heaviest 100 were 98 hated and 2 loved.
        var given = CrowdShapeRules.DotsPerSide(new Dictionary<Sentiment, (int, double)>
        {
            [Sentiment.Positive] = (113, 22.3),
            [Sentiment.Negative] = (255, 77.6),
            [Sentiment.Neutral] = (0, 0.1)
        }, 100);

        Assert.Equal(22, given[Sentiment.Positive]);
        Assert.Equal(78, given[Sentiment.Negative]);
        Assert.Equal(0, given[Sentiment.Neutral]);   // no posts, no dots
    }

    [Fact]
    public void A_small_side_still_gets_a_dot_and_a_short_side_passes_its_room_on()
    {
        var given = CrowdShapeRules.DotsPerSide(new Dictionary<Sentiment, (int, double)>
        {
            [Sentiment.Positive] = (3, 39.5),
            [Sentiment.Negative] = (50, 60),
            [Sentiment.Neutral] = (1, 0.5)
        }, 20);

        Assert.Equal(1, given[Sentiment.Neutral]);
        Assert.Equal(3, given[Sentiment.Positive]);   // all it has
        Assert.Equal(16, given[Sentiment.Negative]);  // the rest
    }

    [Fact]
    public void Listed_titles_match_whatever_the_case()
    {
        var options = new CrowdShapeOptions { Titles = { "movie_1400837" } };
        Assert.True(options.IsListed("MOVIE_1400837"));
        Assert.False(options.IsListed("tv_124364"));
    }

    // ---- the service ----

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

    /// <summary>Adds posts for a title and the snapshot row the service checks; returns the snapshot.</summary>
    private static async Task<TitleSnapshot> AddTitleAsync(SocialMeterDbContext db, int tmdbId, int positive, int negative)
    {
        for (var i = 0; i < positive; i++) db.SocialPosts.Add(Post(tmdbId, Sentiment.Positive, 0.8, platform: i % 2 == 0 ? "x" : "bluesky"));
        for (var i = 0; i < negative; i++) db.SocialPosts.Add(Post(tmdbId, Sentiment.Negative, -0.6));
        var snapshot = new TitleSnapshot { TmdbId = tmdbId, MediaType = MediaType.Movie, TotalPosts = positive + negative, LastRefreshedAt = DateTime.UtcNow };
        db.TitleSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }

    private static CrowdShapeService Service(SocialMeterDbContext db, CrowdShapeOptions options, WatchOptions? watch = null, IMemoryCache? cache = null)
    {
        cache ??= new MemoryCache(new MemoryCacheOptions());
        var social = Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions { FeaturedWindowDays = 3, FeaturedMinPosts = 1 });
        var queries = new PostQueryService(db, new ScoreHistoryService(db, new PullGate(), social));
        return new CrowdShapeService(db, Microsoft.Extensions.Options.Options.Create(options),
            new WatchWindows(new TestOptionsMonitor<WatchOptions>(watch ?? new WatchOptions()), TimeProvider.System),
            new MostTalkedAbout(queries, social, cache), cache);
    }

    [Fact]
    public async Task A_title_that_is_not_listed_live_or_talked_about_gets_nothing()
    {
        var db = Db();
        await AddTitleAsync(db, 1, 30, 5);

        var result = await Service(db, new CrowdShapeOptions { TopTalkedAbout = 0 }).GetAsync(MediaType.Movie, 1);

        Assert.False(result.Enabled);
        Assert.Empty(result.Dots);
    }

    [Fact]
    public async Task The_switch_turns_it_off_even_for_a_listed_title()
    {
        var db = Db();
        await AddTitleAsync(db, 1, 6, 5);

        var result = await Service(db, new CrowdShapeOptions { Enabled = false, Titles = { "movie_1" } }).GetAsync(MediaType.Movie, 1);

        Assert.False(result.Enabled);
    }

    [Fact]
    public async Task A_listed_title_gets_every_counted_post_as_a_dot_and_its_shape()
    {
        var db = Db();
        await AddTitleAsync(db, 1, 6, 5);
        db.SocialPosts.Add(Post(1, Sentiment.Positive, 0.9, upvotes: 5000, excluded: true));   // filtered: never a dot
        await AddTitleAsync(db, 2, 0, 1);                                                    // another title
        await db.SaveChangesAsync();

        var result = await Service(db, new CrowdShapeOptions { Titles = { "movie_1" }, TopTalkedAbout = 0 }).GetAsync(MediaType.Movie, 1);

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
    public async Task A_live_event_title_shows_without_being_listed()
    {
        var db = Db();
        await AddTitleAsync(db, 7, 4, 1);
        var now = DateTimeOffset.UtcNow;
        var watch = new WatchOptions { Windows = { new WatchWindow { MediaType = MediaType.Movie, TmdbId = 7, Label = "Watch party tonight", StartsAt = now.AddHours(-1), EndsAt = now.AddHours(2) } } };

        var result = await Service(db, new CrowdShapeOptions { TopTalkedAbout = 0 }, watch).GetAsync(MediaType.Movie, 7);

        Assert.True(result.Enabled);
        Assert.Equal(CrowdShapeRules.Early, result.Shape);   // five posts: shown, but too few to call
        Assert.True(result.Live);
        Assert.Equal("Watch party tonight", result.LiveLabel);
    }

    // ---- the crowd over time ----

    private static CrowdShapeRules.TimelinePost Moment(int minute, double value, double weight = 1) =>
        new(new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc).AddMinutes(minute), value, weight, value >= 0 ? "positive" : "negative", "x");

    [Fact]
    public void Too_few_recent_posts_draw_no_line()
    {
        var posts = Enumerable.Range(0, CrowdShapeRules.TimelineMinPosts - 1).Select(i => Moment(i, 0.9)).ToList();

        Assert.Empty(CrowdShapeRules.Timeline(posts));
    }

    [Fact]
    public void The_line_follows_the_latest_posts_in_the_order_they_were_posted()
    {
        // Twelve loved-it posts, then twelve hated-it ones, handed over out of order.
        var posts = Enumerable.Range(0, 12).Select(i => Moment(i, 0.9))
            .Concat(Enumerable.Range(12, 12).Select(i => Moment(i, -0.8)))
            .Reverse()
            .ToList();

        var line = CrowdShapeRules.Timeline(posts);

        Assert.Equal(24 - (CrowdShapeRules.TimelineStart - 1), line.Count);   // starts at the third post
        Assert.True(line.Zip(line.Skip(1)).All(p => p.First.At < p.Second.At));
        Assert.Equal(95, line[0].Mood);       // all loved it: (0.9 + 1) x 50
        Assert.Equal(10, line[^1].Mood);      // the window has moved past every loved-it post: (-0.8 + 1) x 50
        Assert.Equal("negative", line[^1].Sentiment);
        Assert.True(line[12].Mood < line[8].Mood);   // turning as the hated-it posts come in
    }

    [Fact]
    public void Heavier_posts_pull_the_line_harder()
    {
        var posts = Enumerable.Range(0, 11).Select(i => Moment(i, -1)).Append(Moment(11, 1, weight: 11)).ToList();

        Assert.Equal(50, CrowdShapeRules.Timeline(posts)[^1].Mood);   // one post weighing as much as the other eleven
    }

    [Fact]
    public async Task The_crowd_carries_a_line_of_the_last_two_days_only()
    {
        var db = Db();
        await AddTitleAsync(db, 1, 8, 4);   // posted a day ago
        for (var i = 0; i < 5; i++)
        {
            var old = Post(1, Sentiment.Positive, 0.8);
            old.CreatedAt = DateTime.UtcNow.AddDays(-5);
            db.SocialPosts.Add(old);
        }
        await db.SaveChangesAsync();

        var result = await Service(db, new CrowdShapeOptions { Titles = { "movie_1" }, TopTalkedAbout = 0 }).GetAsync(MediaType.Movie, 1);

        Assert.Equal(17, result.Posts);
        Assert.Equal(12 - (CrowdShapeRules.TimelineStart - 1), result.Timeline.Count);
        Assert.Equal(CrowdShapeRules.TimelineHours, result.TimelineHours);
        Assert.False(result.Live);
        Assert.Null(result.LiveLabel);
    }

    [Fact]
    public async Task Talked_about_titles_show_once_they_have_enough_posts()
    {
        var db = Db();
        await AddTitleAsync(db, 1, 20, 10);   // the most talked about, 30 posts
        await AddTitleAsync(db, 2, 8, 2);     // second, only 10 posts
        await AddTitleAsync(db, 3, 3, 1);     // third: outside the top 2
        var options = new CrowdShapeOptions { TopTalkedAbout = 2, MinPosts = 20 };

        Assert.True((await Service(db, options).GetAsync(MediaType.Movie, 1)).Enabled);
        Assert.False((await Service(db, options).GetAsync(MediaType.Movie, 2)).Enabled);
        Assert.False((await Service(db, options).GetAsync(MediaType.Movie, 3)).Enabled);
    }

    [Fact]
    public async Task The_crowd_is_built_once_per_reading_and_rebuilt_when_the_reading_changes()
    {
        var db = Db();
        var snapshot = await AddTitleAsync(db, 1, 6, 5);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new CrowdShapeOptions { Titles = { "movie_1" }, TopTalkedAbout = 0 };

        Assert.Equal(11, (await Service(db, options, cache: cache).GetAsync(MediaType.Movie, 1)).Posts);

        // A post arrives but the reading hasn't been recomputed yet: the cached crowd is still served.
        db.SocialPosts.Add(Post(1, Sentiment.Positive, 0.8));
        await db.SaveChangesAsync();
        Assert.Equal(11, (await Service(db, options, cache: cache).GetAsync(MediaType.Movie, 1)).Posts);

        // The reading is recomputed (a new read, or a post hidden): the crowd is rebuilt.
        snapshot.TotalPosts = 12;
        await db.SaveChangesAsync();
        Assert.Equal(12, (await Service(db, options, cache: cache).GetAsync(MediaType.Movie, 1)).Posts);
    }

    [Fact]
    public async Task Only_the_heaviest_posts_are_drawn_when_there_are_too_many_but_the_shares_use_all()
    {
        var db = Db();
        db.SocialPosts.Add(Post(1, Sentiment.Negative, -0.8, upvotes: 900));
        await AddTitleAsync(db, 1, 12, 0);

        var result = await Service(db, new CrowdShapeOptions { Titles = { "movie_1" }, TopTalkedAbout = 0, MaxDots = 3 }).GetAsync(MediaType.Movie, 1);

        Assert.Equal(13, result.Posts);
        Assert.Equal(3, result.Dots.Count);
        Assert.Equal("negative", result.Dots[0].Sentiment);   // the heaviest first
    }

    [Fact]
    public async Task Posts_can_be_listed_most_liked_newest_or_oldest_first()
    {
        var db = Db();
        var old = Post(1, Sentiment.Positive, 0.8, upvotes: 50);
        old.CreatedAt = DateTime.UtcNow.AddDays(-5);
        var mid = Post(1, Sentiment.Negative, -0.5, upvotes: 1);
        mid.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var fresh = Post(1, Sentiment.Positive, 0.6, upvotes: 10);
        fresh.CreatedAt = DateTime.UtcNow.AddHours(-1);
        db.SocialPosts.AddRange(old, mid, fresh);
        await db.SaveChangesAsync();
        var queries = new PostQueryService(db, new ScoreHistoryService(db, new PullGate(), Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions())));

        async Task<int[]> Ids(Application.Abstractions.PostSort sort) =>
            (await queries.GetPostsAsync(MediaType.Movie, 1, null, null, 1, 12, sort)).Items.Select(p => p.Id).ToArray();

        Assert.Equal(new[] { old.Id, fresh.Id, mid.Id }, await Ids(Application.Abstractions.PostSort.Top));
        Assert.Equal(new[] { fresh.Id, mid.Id, old.Id }, await Ids(Application.Abstractions.PostSort.Newest));
        Assert.Equal(new[] { old.Id, mid.Id, fresh.Id }, await Ids(Application.Abstractions.PostSort.Oldest));
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
