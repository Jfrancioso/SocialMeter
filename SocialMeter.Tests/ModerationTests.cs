using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SocialMeter.Application.Moderation;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

public class ModerationTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    private static SocialPost P(double sentiment, string author = "a", string? exclusionReason = null, int tmdbId = 7) => new()
    {
        TmdbId = tmdbId, MediaType = MediaType.Movie, Platform = "x", ExternalId = Guid.NewGuid().ToString("N"),
        Author = author, AuthorId = author, Content = $"post by {author} {Guid.NewGuid():N}", ContentHash = Guid.NewGuid().ToString("N"),
        SentimentScore = sentiment, Sentiment = SentimentResult.FromScore(sentiment).Label, TrustWeight = 1, Substance = 0.5,
        Excluded = exclusionReason is not null, ExclusionReason = exclusionReason, CreatedAt = Now.AddDays(-1), FetchedAt = Now
    };

    // ---- The rule itself ----

    [Fact]
    public void Hiding_excludes_the_post_and_keeps_the_filters_verdict_underneath()
    {
        var post = P(0.8);
        Assert.True(PostModeration.Hide(post, "sensitive", Now));
        Assert.True(post.Excluded);
        Assert.True(post.Hidden);
        Assert.Null(post.ExclusionReason);
        Assert.False(PostModeration.Hide(post, "spam", Now));   // already hidden: nothing changes
        Assert.Equal("sensitive", post.HiddenReason);
    }

    [Fact]
    public void Restoring_puts_the_post_back_where_the_filters_had_it()
    {
        var counted = P(0.8);
        var filtered = P(0.8, exclusionReason: TrustPolicy.ReasonTooNew);
        PostModeration.Hide(counted, "spam", Now);
        PostModeration.Hide(filtered, "spam", Now);

        Assert.True(PostModeration.Restore(counted));
        Assert.True(PostModeration.Restore(filtered));
        Assert.False(counted.Excluded);
        Assert.True(filtered.Excluded);   // was filtered before the hide, still is
        Assert.False(PostModeration.Restore(counted));
    }

    [Fact]
    public void Unknown_reasons_are_refused()
    {
        Assert.Throws<ArgumentException>(() => PostModeration.Hide(P(0.5), "because", Now));
        Assert.False(PostModeration.IsValidReason(null));
    }

    [Fact]
    public void Cross_post_rules_never_bring_a_hidden_post_back()
    {
        // Two posts from one account: the busier one counts. Hide it, and the other one should count instead.
        var busy = P(0.9, author: "fan");
        busy.Upvotes = 50;
        var quiet = P(0.4, author: "fan");
        var posts = new[] { busy, quiet };
        TrustPolicy.ApplyCrossPostRules(posts, Now);
        Assert.False(busy.Excluded);
        Assert.True(quiet.Excluded);

        PostModeration.Hide(busy, "sensitive", Now);
        TrustPolicy.ApplyCrossPostRules(posts, Now);

        Assert.True(busy.Excluded);
        Assert.False(quiet.Excluded);
    }

    // ---- The service against a database ----

    private static (ModerationService Service, SocialMeterDbContext Db) Service()
    {
        var db = new SocialMeterDbContext(new DbContextOptionsBuilder<SocialMeterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions());
        var gate = new PullGate();
        var recomputer = new TitleRecomputer(db, new ScoreHistoryService(db, gate, options), options);
        return (new ModerationService(db, recomputer, gate, NullLogger<ModerationService>.Instance), db);
    }

    private static async Task<List<SocialPost>> SeedAsync(SocialMeterDbContext db)
    {
        var posts = Enumerable.Range(0, 12).Select(i => P(0.9, author: $"fan{i}")).Append(P(-0.9, author: "troll")).ToList();
        db.SocialPosts.AddRange(posts);
        var meter = SocialScoreCalculator.Compute(posts, DateTime.UtcNow, new SocialMeterOptions().Scoring);   // as the service scores
        var snapshot = new TitleSnapshot { TmdbId = 7, MediaType = MediaType.Movie, LastRefreshedAt = DateTime.UtcNow };
        snapshot.ApplyMeter(meter, snapshot.LastRefreshedAt);
        db.TitleSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return posts;
    }

    [Fact]
    public async Task Hiding_rescores_the_title_and_logs_who_did_it()
    {
        var (sut, db) = Service();
        var posts = await SeedAsync(db);
        var before = (await db.TitleSnapshots.SingleAsync()).Score;
        var troll = posts.Last();

        var result = await sut.HideAsync(troll.Id, "sensitive", "Test Movie", "joey@example.com");

        Assert.NotNull(result);
        Assert.True(result!.Hidden && result.Changed);
        Assert.True(result.Score > before, $"hiding the one negative post should lift {before}, got {result.Score}");
        Assert.Equal(result.Score, (await db.TitleSnapshots.SingleAsync()).Score);

        var log = Assert.Single(await sut.RecentAsync(10));
        Assert.Equal("hide", log.Action);
        Assert.Equal("sensitive", log.Reason);
        Assert.Equal("joey@example.com", log.Actor);
        Assert.Equal("Test Movie", log.TitleName);
        Assert.True(log.CurrentlyHidden);
    }

    [Fact]
    public async Task Restoring_undoes_the_hide_and_reuses_the_title_name()
    {
        var (sut, db) = Service();
        var posts = await SeedAsync(db);
        var before = (await db.TitleSnapshots.SingleAsync()).Score;
        var troll = posts.Last();

        await sut.HideAsync(troll.Id, "spam", "Test Movie", "joey@example.com");
        var restored = await sut.RestoreAsync(troll.Id, "joey@example.com");

        Assert.False(restored!.Hidden);
        Assert.Equal(before, restored.Score);
        var log = await sut.RecentAsync(10);
        Assert.Equal(new[] { "restore", "hide" }, log.Select(l => l.Action));
        Assert.All(log, l => Assert.Equal("Test Movie", l.TitleName));
        Assert.All(log, l => Assert.False(l.CurrentlyHidden));
    }

    [Fact]
    public async Task Repeating_a_hide_changes_nothing_and_logs_nothing()
    {
        var (sut, db) = Service();
        var posts = await SeedAsync(db);
        await sut.HideAsync(posts[0].Id, "spam", null, "admin key");
        var again = await sut.HideAsync(posts[0].Id, "spam", null, "admin key");

        Assert.False(again!.Changed);
        Assert.Single(await sut.RecentAsync(10));
    }

    [Fact]
    public async Task A_missing_post_is_null_not_an_error()
    {
        var (sut, _) = Service();
        Assert.Null(await sut.HideAsync(12345, "spam", null, "admin key"));
        Assert.Null(await sut.RestoreAsync(12345, "admin key"));
    }
}
