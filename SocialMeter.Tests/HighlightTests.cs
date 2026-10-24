using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using Xunit;

namespace SocialMeter.Tests;

/// <summary>"In their words": which side is featured and which post. The texts are made up, not real posts.</summary>
public class HighlightTests
{
    private const string Review = "The practical effects in the transformation scenes are the best I've seen in years, and the lead is haunting.";

    private static int _ids;

    private static SocialPost Post(string content = Review, Sentiment sentiment = Sentiment.Positive, double substance = 0.7,
        bool sensitive = false, bool excluded = false, int upvotes = 10, int ageDays = 1, double? tone = null) => new()
    {
        Id = Interlocked.Increment(ref _ids),
        TmdbId = 1,
        MediaType = MediaType.Movie,
        Platform = "bluesky",
        ExternalId = Guid.NewGuid().ToString(),
        Author = "someone",
        Content = content,
        Sentiment = sentiment,
        SentimentScore = tone ?? (sentiment == Sentiment.Negative ? -0.7 : 0.7),
        Substance = substance,
        TrustWeight = 1,
        Sensitive = sensitive,
        Excluded = excluded,
        Upvotes = upvotes,
        CreatedAt = DateTime.UtcNow.AddDays(-ageDays)
    };

    private static SocialPost WithMedia(SocialPost post, string mediaJson)
    {
        post.MediaJson = mediaJson;
        return post;
    }

    [Fact]
    public void A_readable_specific_post_is_quotable()
        => Assert.True(HighlightRules.IsQuotable(Post()));

    [Theory]
    [InlineData("10/10 peak cinema, go see it right now!!!")]                                         // too short
    [InlineData("MILD SPOILER: the scene in the diner is one of my favourite movie scenes of all time, honestly.")]
    [InlineData("No spoilers but the last twenty minutes completely changed how I felt about the whole movie.")]
    [InlineData("This is so fucking good, the effects in the transformation scenes are unreal and the lead is great.")]
    [InlineData("What a load of bullshit, the second half throws away everything the first half built so carefully.")]
    [InlineData("Proof that the new cast can play bitchy mean girls just as well as the original, and the finale is great.")]
    [InlineData("Fix the fking servers already, the matchmaking has been broken for three days and nobody says anything.")]
    [InlineData("THIS IS THE WORST UPDATE EVER, THE SERVERS ARE DOWN AGAIN AND NOBODY AT THE STUDIO EVEN CARES ANYMORE.")]
    [InlineData("The villain is basically a pedo and the movie never deals with it, which made the ending feel hollow.")]
    [InlineData("Kick ass monster movie with great practical effects and a lead performance that sticks with you.")]
    [InlineData("Review https://example.com/a-very-long-link-to-a-review-site-article-about-the-movie #horror #film @critic")]
    public void Posts_that_should_not_be_featured_are_skipped(string content)
        => Assert.False(HighlightRules.IsQuotable(Post(content)));

    [Fact]
    public void Words_that_merely_contain_a_short_blocked_word_are_fine()
        // "class", "assassin", "Dickens", "cocktail": only whole words are blocked for the short ones.
        => Assert.True(HighlightRules.IsQuotable(Post("A classy assassin thriller with a Dickens-sized cast and a cocktail-bar shootout that rules.")));

    [Fact]
    public void A_title_in_capitals_is_fine_when_the_post_is_not_shouting()
        => Assert.True(HighlightRules.IsQuotable(Post("Saw OTHER MOMMY last night. The first half is genuinely unsettling and the lead is excellent.")));

    [Fact]
    public void Bare_sensitive_excluded_and_overlong_posts_are_skipped()
    {
        Assert.False(HighlightRules.IsQuotable(Post(substance: 0.2)));
        Assert.False(HighlightRules.IsQuotable(Post(sensitive: true)));
        Assert.False(HighlightRules.IsQuotable(Post(excluded: true)));
        var discounted = Post();
        discounted.TrustWeight = 0.6;   // a young account: counts in the score, never featured
        Assert.False(HighlightRules.IsQuotable(discounted));
        Assert.False(HighlightRules.IsQuotable(WithMedia(Post(), """[{"type":"video","url":"https://example.com/v.mp4"}]""")));
        Assert.True(HighlightRules.IsQuotable(WithMedia(Post(), "[]")));
        Assert.False(HighlightRules.IsQuotable(Post(string.Concat(Enumerable.Repeat(Review + " ", 6)))));
    }

    [Fact]
    public void Tally_counts_each_side_against_the_whole_weight_including_neutral()
    {
        var tallies = HighlightRules.Tally(new[]
        {
            (Sentiment.Positive, 3.0), (Sentiment.Positive, 3.0), (Sentiment.Negative, 2.0), (Sentiment.Neutral, 2.0)
        });

        Assert.Equal(new SideTally(Sentiment.Positive, 2, 60), tallies[0]);
        Assert.Equal(new SideTally(Sentiment.Negative, 1, 20), tallies[1]);
    }

    [Fact]
    public void A_side_needs_enough_posts_and_enough_weight()
    {
        Assert.True(HighlightRules.Featured(new SideTally(Sentiment.Negative, 3, 10)));
        Assert.False(HighlightRules.Featured(new SideTally(Sentiment.Negative, 2, 40)));   // two complaints, however heavy
        Assert.False(HighlightRules.Featured(new SideTally(Sentiment.Negative, 30, 8)));   // many posts, little of the reading
    }

    [Fact]
    public void Pick_takes_the_heaviest_quotable_post_and_skips_heavier_ones_that_fail()
    {
        var heavyButBare = Post(substance: 0.1);
        var heavyButSpoiler = Post("Spoilers: the diner scene is a masterpiece of tension and I can't stop thinking about it.");
        var best = Post();
        var lighter = Post();

        var pick = HighlightRules.Pick(new[] { (lighter, 1.0), (heavyButBare, 9.0), (best, 5.0), (heavyButSpoiler, 8.0) });

        Assert.Same(best, pick);
    }

    [Fact]
    public void A_clear_take_beats_a_vaguer_post_with_a_little_more_weight()
    {
        // The real case: a political aside read as mildly positive (+0.4) narrowly out-weighed "season 2 is even better" (+0.9).
        var aside = Post("Much like that other series, season two is looking like it is going to be full of a certain politics.", tone: 0.4);
        var take = Post("Season two is somehow even better than the first. Wild. Love the car cameos all over again too.", tone: 0.9);

        Assert.Same(take, HighlightRules.Pick(new[] { (aside, 11.6), (take, 11.4) }));
    }

    [Fact]
    public void Rank_uses_the_strength_of_the_lean_on_either_side()
    {
        Assert.Equal(4.5, HighlightRules.Rank(Post(tone: -0.9), 5), 6);
        Assert.Equal(5, HighlightRules.Rank(Post(tone: 3), 5), 6);   // out-of-range tones are clamped
    }

    [Fact]
    public void Pick_returns_null_when_nothing_qualifies()
        => Assert.Null(HighlightRules.Pick(new[] { (Post(substance: 0.1), 1.0) }));

    [Fact]
    public async Task The_service_features_each_qualifying_side_and_leaves_a_small_side_out()
    {
        var db = new SocialMeterDbContext(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var top = Post(upvotes: 500);
        var clip = WithMedia(Post(upvotes: 700), """[{"type":"video","url":"https://example.com/v.mp4"}]""");
        db.SocialPosts.AddRange(top, Post(), clip, Post(upvotes: 900, excluded: true));
        db.SocialPosts.AddRange(Post("The story falls apart in the second half and the pacing drags through the whole middle stretch.", Sentiment.Negative),
            Post("Two hours of nothing happening, with a lead who never gets a single scene worth remembering at all.", Sentiment.Negative));
        await db.SaveChangesAsync();

        var result = await new TitleHighlights(db).GetAsync(MediaType.Movie, 1);

        Assert.Equal(top.Id, result.Positive?.Post?.Id);   // the excluded post and the clip are heavier but never featured
        Assert.Equal(3, result.Positive?.Posts);
        Assert.True(result.Positive?.Featured);
        Assert.Equal(2, result.Negative?.Posts);            // two negative posts: counted, not featured
        Assert.False(result.Negative?.Featured);
        Assert.Null(result.Negative?.Post);
    }

    [Fact]
    public async Task A_title_with_no_counted_posts_has_no_highlights()
    {
        var db = new SocialMeterDbContext(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var result = await new TitleHighlights(db).GetAsync(MediaType.Movie, 1);
        Assert.Null(result.Positive);
        Assert.Null(result.Negative);
    }
}
