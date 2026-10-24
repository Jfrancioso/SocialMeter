using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Home;
using SocialMeter.Application.Outbox;
using SocialMeter.Application.Social;
using SocialMeter.Application.Tracking;
using SocialMeter.Contracts.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Bluesky;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

/// <summary>The brand account's Outbox: what a post says, which title gets drafted when, and that only a tap posts.</summary>
public class OutboxTests
{
    // ---- the wording ----

    [Fact]
    public void A_post_is_facts_then_the_link()
    {
        var text = OutboxRules.Text(new OutboxRules.Facts("Buddy", 2026, 69, "Hot", 36, 74.2, 21.6, null), "chatterbub.com/title/movie/1514026");

        Assert.Equal("Buddy (2026)\nChatterbub score 69 · Hot\n74% loved it · 22% hated it · 36 posts\nchatterbub.com/title/movie/1514026", text);
    }

    [Fact]
    public void A_live_event_leads_with_its_label_and_a_crowd_that_isnt_drawn_gives_just_the_count()
    {
        var text = OutboxRules.Text(new OutboxRules.Facts("Buddy", null, 69, "Hot", 1, null, null, "Watch party tonight"), "x.com/1");

        Assert.Equal("Live now: Watch party tonight\nBuddy\nChatterbub score 69 · Hot\n1 post\nx.com/1", text);
    }

    [Fact]
    public void Even_a_long_name_fits_both_bluesky_and_x()
    {
        var name = new string('A', 150);
        var text = OutboxRules.Text(new OutboxRules.Facts(name, 2026, 100, "Electric", 12345, 99.9, 0.1, "A very long event label for a premiere"),
            OutboxRules.Display(OutboxRules.TitleUrl("https://chatterbub.com", MediaType.Movie, 2147483647)));

        Assert.True(text.Length <= OutboxRules.MaxTextLength, $"{text.Length} characters");
        Assert.Contains("…", text);
    }

    // ---- which title, when ----

    private static OutboxRules.Candidate C(int id, string shape = "loved", int quotable = 0, MediaType type = MediaType.Movie) => new(type, id, shape, quotable);

    [Fact]
    public void The_daily_draft_prefers_both_sides_quotable_then_a_split_crowd_then_the_most_talked_about()
    {
        var none = new HashSet<(MediaType, int)>();

        Assert.Equal(2, OutboxRules.PickDaily(new[] { C(1), C(2, CrowdShapeRules.Split), C(3) }, none)!.TmdbId);
        Assert.Equal(3, OutboxRules.PickDaily(new[] { C(1), C(2, CrowdShapeRules.Split, quotable: 1), C(3, quotable: 2) }, none)!.TmdbId);
        Assert.Equal(1, OutboxRules.PickDaily(new[] { C(1), C(2, quotable: 1) }, none)!.TmdbId);   // one side isn't enough to jump the order
        Assert.Equal(OutboxItem.Split, OutboxRules.KindFor(C(2, CrowdShapeRules.Split)));
        Assert.Equal(OutboxItem.TalkedAbout, OutboxRules.KindFor(C(1)));
    }

    [Fact]
    public void The_daily_draft_never_repeats_too_soon_and_never_picks_a_game()
    {
        var ranked = new[] { C(1, type: MediaType.Game, quotable: 2), C(2, CrowdShapeRules.Split), C(3) };

        Assert.Equal(2, OutboxRules.PickDaily(ranked, new HashSet<(MediaType, int)>())!.TmdbId);
        Assert.Equal(3, OutboxRules.PickDaily(ranked, new HashSet<(MediaType, int)> { (MediaType.Movie, 2) })!.TmdbId);
        Assert.Null(OutboxRules.PickDaily(new[] { C(1) }, new HashSet<(MediaType, int)> { (MediaType.Movie, 1) }));
    }

    [Fact]
    public void The_daily_draft_is_due_once_a_day_from_the_draft_hour()
    {
        var morning = new DateTime(2026, 10, 24, 13, 0, 0, DateTimeKind.Utc);
        var afternoon = morning.AddHours(2);

        Assert.False(OutboxRules.DailyDue(morning, null, 14));
        Assert.True(OutboxRules.DailyDue(afternoon, null, 14));
        Assert.False(OutboxRules.DailyDue(afternoon, afternoon.AddMinutes(-30), 14));   // already drafted today
        Assert.True(OutboxRules.DailyDue(afternoon, afternoon.AddDays(-1), 14));
    }

    // ---- the Bluesky records ----

    [Fact]
    public void Links_are_marked_by_utf8_byte_offsets_not_characters()
    {
        var text = "Amélie (2001) · 88\nchatterbub.com/title/movie/194";

        var range = BlueskyRecords.ByteRange(text, "chatterbub.com/title/movie/194");

        // "é" and "·" take two bytes each in UTF-8, so the link starts two bytes later than its character index.
        Assert.Equal((text.IndexOf("chatterbub", StringComparison.Ordinal) + 2, text.IndexOf("chatterbub", StringComparison.Ordinal) + 2 + 30), range);
        Assert.Null(BlueskyRecords.ByteRange(text, "not there"));
    }

    [Fact]
    public void The_post_carries_its_link_and_card_and_the_replies_quote_in_a_thread()
    {
        var now = new DateTime(2026, 10, 24, 15, 0, 0, DateTimeKind.Utc);
        var thumb = JsonNode.Parse("""{"$type":"blob","ref":{"$link":"bafk"},"mimeType":"image/jpeg","size":1234}""");

        var post = BlueskyRecords.Post("Buddy\nchatterbub.com/title/movie/1", "chatterbub.com/title/movie/1", "https://chatterbub.com/title/movie/1",
            "Buddy · Chatterbub score 69", "74% loved it", thumb, now);
        var reply = BlueskyRecords.QuoteReply(OutboxRules.CaseFor, ("at://me/app.bsky.feed.post/1", "c1"), ("at://me/app.bsky.feed.post/1", "c1"),
            ("at://them/app.bsky.feed.post/9", "c9"), now);

        Assert.Equal("app.bsky.feed.post", (string)post["$type"]!);
        Assert.Equal("2026-10-24T15:00:00.000Z", (string)post["createdAt"]!);
        Assert.Equal("https://chatterbub.com/title/movie/1", (string)post["facets"]![0]!["features"]![0]!["uri"]!);
        Assert.Equal(6, (int)post["facets"]![0]!["index"]!["byteStart"]!);
        Assert.Equal("app.bsky.embed.external", (string)post["embed"]!["$type"]!);
        Assert.Equal("bafk", (string)post["embed"]!["external"]!["thumb"]!["ref"]!["$link"]!);

        Assert.Equal("The case for:", (string)reply["text"]!);
        Assert.Equal("c1", (string)reply["reply"]!["root"]!["cid"]!);
        Assert.Equal("app.bsky.embed.record", (string)reply["embed"]!["$type"]!);
        Assert.Equal("at://them/app.bsky.feed.post/9", (string)reply["embed"]!["record"]!["uri"]!);
        Assert.Equal("https://bsky.app/profile/did:plc:abc/post/3kx", BlueskyRecords.WebUrl("at://did:plc:abc/app.bsky.feed.post/3kx"));
    }

    // ---- the service ----

    private static readonly DateTime Now = new(2026, 10, 24, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>The watch windows' clock, pinned to the same moment as the drafts.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakePoster : IBlueskyPoster
    {
        public bool IsConfigured { get; set; } = true;
        public string? Account => IsConfigured ? "chatterbub.bsky.social" : null;
        public List<BlueskyThread> Sent { get; } = new();
        public bool Fail { get; set; }

        public Task<BlueskyPostResult> PostThreadAsync(BlueskyThread thread, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("Bluesky refused the post (500): down");
            Sent.Add(thread);
            return Task.FromResult(new BlueskyPostResult($"at://did:plc:brand/app.bsky.feed.post/{Sent.Count}", "https://bsky.app/x", thread.Quotes.Count, 0));
        }
    }

    private sealed class FakeCrowd : ICrowdShape
    {
        public Dictionary<int, string> Shapes { get; } = new();
        public Task<CrowdShapeDto> GetAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default) =>
            Task.FromResult(Shapes.TryGetValue(tmdbId, out var shape)
                ? new CrowdShapeDto { Enabled = true, Shape = shape, Posts = 30, Positive = 60, Negative = 35, Neutral = 5 }
                : new CrowdShapeDto { Enabled = false });
    }

    private sealed class FakeTitles : ITitleService
    {
        public Task<Title?> GetDetailsAsync(MediaType mediaType, int id, CancellationToken ct = default) =>
            Task.FromResult<Title?>(new Title { TmdbId = id, MediaType = mediaType, Name = $"Movie {id}", ReleaseDate = new DateOnly(2026, 10, 1), BackdropPath = "/b.jpg" });

        public Task<PagedResult<Title>> GetTrendingAsync(MediaType? mediaType, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetNowPlayingAsync(string region, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetOnTheAirAsync(int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetPopularAsync(MediaType mediaType, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetTopRatedAsync(MediaType mediaType, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetUpcomingAsync(string region, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> DiscoverByGenreAsync(MediaType mediaType, int genreId, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> DiscoverTvByKeywordAsync(int keywordId, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> DiscoverRecentMoviesByGenreAsync(int genreId, int days, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> DiscoverInTheatersByGenreAsync(int genreId, string region, int days, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetNewGamesAsync(int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> GetComingSoonGamesAsync(int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Title>> SearchAsync(string query, MediaType? mediaType, int page = 1, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static SocialMeterDbContext Db() =>
        new(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SocialPost Post(int tmdbId, Sentiment sentiment, string platform, string content, int upvotes = 5) => new()
    {
        TmdbId = tmdbId,
        MediaType = MediaType.Movie,
        Platform = platform,
        ExternalId = $"did:plc:author{Guid.NewGuid():N}/3k{upvotes}",
        Author = "someone",
        Content = content,
        Sentiment = sentiment,
        SentimentScore = sentiment == Sentiment.Positive ? 0.8 : -0.7,
        TrustWeight = 1,
        Substance = 0.6,
        Upvotes = upvotes,
        CreatedAt = Now.AddHours(-6)
    };

    private const string Praise = "Honestly one of the most fun horror comedies in years, the unicorn performance is unhinged and perfect.";
    private const string Pan = "Wanted to love it but the second half drags badly and the jokes stop landing after the first act ends.";

    /// <summary>Titles 1-3 with posts and readings; title 2 has Bluesky posts on both sides.</summary>
    private static async Task SeedAsync(SocialMeterDbContext db)
    {
        foreach (var id in new[] { 1, 2, 3 })
        {
            for (var i = 0; i < 5 - id; i++) db.SocialPosts.Add(Post(id, Sentiment.Positive, "x", "A post about the movie that is long enough to count here."));
            db.TitleSnapshots.Add(new TitleSnapshot { TmdbId = id, MediaType = MediaType.Movie, Score = 60 + id, Label = "Warm", TotalPosts = 10, LastRefreshedAt = Now.AddHours(-1) });
        }
        db.SocialPosts.Add(Post(2, Sentiment.Positive, "bluesky", Praise, upvotes: 40));
        db.SocialPosts.Add(Post(2, Sentiment.Negative, "bluesky", Pan, upvotes: 30));
        db.SocialPosts.Add(Post(2, Sentiment.Negative, "x", Pan + " On X.", upvotes: 900));   // can't be quoted on Bluesky
        await db.SaveChangesAsync();
    }

    private static (OutboxService Service, FakePoster Poster, FakeCrowd Crowd) Service(SocialMeterDbContext db, PostingOptions? options = null, WatchOptions? watch = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var social = Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions { FeaturedWindowDays = 3, FeaturedMinPosts = 1 });
        var queries = new PostQueryService(db, new ScoreHistoryService(db, new PullGate(), social));
        var poster = new FakePoster();
        var crowd = new FakeCrowd();
        var service = new OutboxService(db, Microsoft.Extensions.Options.Options.Create(options ?? new PostingOptions()), new MostTalkedAbout(queries, social, cache), crowd,
            new WatchWindows(new TestOptionsMonitor<WatchOptions>(watch ?? new WatchOptions()), new FixedClock()), new FakeTitles(), poster,
            NullLogger<OutboxService>.Instance);
        return (service, poster, crowd);
    }

    [Fact]
    public async Task The_daily_draft_picks_the_split_title_and_its_strongest_bluesky_post_on_each_side()
    {
        var db = Db();
        await SeedAsync(db);
        var (sut, _, crowd) = Service(db);
        crowd.Shapes[2] = CrowdShapeRules.Split;

        Assert.Equal(1, await sut.DraftDueAsync(Now));
        Assert.Equal(0, await sut.DraftDueAsync(Now.AddHours(1)));   // once a day

        var item = await db.OutboxItems.SingleAsync();
        Assert.Equal((OutboxItem.Split, 2, "Movie 2"), (item.Kind, item.TmdbId, item.TitleName));
        Assert.Equal(Praise, (await db.SocialPosts.FindAsync(item.ForPostId))!.Content);
        Assert.Equal(Pan, (await db.SocialPosts.FindAsync(item.AgainstPostId))!.Content);   // the Bluesky one, not the bigger X post
    }

    [Fact]
    public async Task Nothing_is_drafted_before_the_draft_hour_or_with_posting_turned_off()
    {
        var db = Db();
        await SeedAsync(db);

        Assert.Equal(0, await Service(db).Service.DraftDueAsync(Now.Date.AddHours(9)));
        Assert.Equal(0, await Service(db, new PostingOptions { Enabled = false }).Service.DraftDueAsync(Now));
        Assert.Empty(db.OutboxItems);
    }

    [Fact]
    public async Task A_live_event_gets_its_own_draft_once()
    {
        var db = Db();
        await SeedAsync(db);
        var start = new DateTimeOffset(Now.AddHours(-1));
        var watch = new WatchOptions { Windows = { new WatchWindow { MediaType = MediaType.Movie, TmdbId = 3, Label = "Watch party tonight", StartsAt = start, EndsAt = start.AddHours(4) } } };
        var (sut, _, _) = Service(db, watch: watch);

        await sut.DraftDueAsync(Now);
        await sut.DraftDueAsync(Now.AddMinutes(10));

        Assert.Single(db.OutboxItems.Where(o => o.Kind == OutboxItem.Event && o.TmdbId == 3));
        var list = await sut.ListAsync(Now);
        Assert.StartsWith("Live now: Watch party tonight\nMovie 3 (2026)", list.Items.First(i => i.Kind == OutboxItem.Event).Text);
    }

    [Fact]
    public async Task Posting_happens_only_on_a_tap_with_current_facts_the_quotes_and_the_limits()
    {
        var db = Db();
        await SeedAsync(db);
        var (sut, poster, crowd) = Service(db, new PostingOptions { MaxPerDay = 1 });
        crowd.Shapes[2] = CrowdShapeRules.Split;
        await sut.DraftDueAsync(Now);
        var item = await db.OutboxItems.SingleAsync();
        Assert.Empty(poster.Sent);   // drafting never posts

        var posted = await sut.PostAsync(item.Id, "joey@example.com", Now);

        Assert.True(posted!.Ok);
        var thread = Assert.Single(poster.Sent);
        Assert.Equal("Movie 2 (2026)\nChatterbub score 62 · Warm\n60% loved it · 35% hated it · 10 posts\nchatterbub.com/title/movie/2", thread.Text);
        Assert.Equal("https://chatterbub.com/title/movie/2", thread.Card.Url);
        Assert.Equal("https://image.tmdb.org/t/p/w780/b.jpg", thread.Card.ImageUrl);
        Assert.Equal(new[] { OutboxRules.CaseFor, OutboxRules.CaseAgainst }, thread.Quotes.Select(q => q.Lead));
        Assert.All(thread.Quotes, q => Assert.StartsWith("at://did:plc:author", q.AtUri));
        Assert.Equal(OutboxItem.Posted, posted.Entry!.Status);
        Assert.Equal(thread.Text, (await db.OutboxItems.SingleAsync()).PostedText);

        // Already posted, and the day's limit is reached.
        Assert.False((await sut.PostAsync(item.Id, "joey@example.com", Now))!.Ok);
        var second = (await sut.DraftNowAsync(Now))!;
        var refused = await sut.PostAsync(second.Id, "joey@example.com", Now);
        Assert.False(refused!.Ok);
        Assert.Contains("limit", refused.Error);
        Assert.Single(poster.Sent);
    }

    [Fact]
    public async Task Without_the_app_password_nothing_posts_and_a_failure_keeps_the_draft_to_try_again()
    {
        var db = Db();
        await SeedAsync(db);
        var (sut, poster, _) = Service(db);
        var item = (await sut.DraftNowAsync(Now))!;

        poster.IsConfigured = false;
        var refused = await sut.PostAsync(item.Id, "joey@example.com", Now);
        Assert.False(refused!.Ok);
        Assert.Contains("app password", refused.Error);

        poster.IsConfigured = true;
        poster.Fail = true;
        var failed = await sut.PostAsync(item.Id, "joey@example.com", Now);
        Assert.False(failed!.Ok);
        Assert.Equal(OutboxItem.Draft, (await db.OutboxItems.SingleAsync()).Status);
        Assert.Contains("refused", (await db.OutboxItems.SingleAsync()).Error);

        poster.Fail = false;
        Assert.True((await sut.PostAsync(item.Id, "joey@example.com", Now))!.Ok);
    }

    [Fact]
    public async Task Skipped_titles_and_stale_drafts_are_not_offered_again()
    {
        var db = Db();
        await SeedAsync(db);
        var (sut, _, _) = Service(db, new PostingOptions { ExpireAfterHours = 36 });
        var first = (await sut.DraftNowAsync(Now))!;
        Assert.True((await sut.SkipAsync(first.Id, "joey@example.com", Now))!.Ok);

        var next = (await sut.DraftNowAsync(Now))!;
        Assert.NotEqual(first.TmdbId, next.TmdbId);   // a skipped title waits out the repeat window too

        var list = await sut.ListAsync(Now.AddHours(40));
        Assert.Equal(OutboxItem.Expired, list.Items.Single(i => i.Id == next.Id).Status);
    }
}
