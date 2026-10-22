using System.Text.Json;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Bluesky;
using Xunit;

namespace SocialMeter.Tests;

public class BlueskyPostsTests
{
    private static readonly string[] Exclude = { "box office", "tickets" };

    private static JsonElement Post(string text, string extraRecord = "", string extraPost = "", string uri = "at://did:plc:abc123/app.bsky.feed.post/3kxyz") =>
        JsonDocument.Parse($$"""
        {
          "uri": "{{uri}}",
          "author": { "did": "did:plc:abc123", "handle": "filmfan.bsky.social" },
          "record": { "$type": "app.bsky.feed.post", "text": {{JsonSerializer.Serialize(text)}}, "createdAt": "2026-10-20T18:00:00Z" {{extraRecord}} },
          "likeCount": 42, "replyCount": 7, "repostCount": 3,
          "indexedAt": "2026-10-20T18:00:05Z"
          {{extraPost}}
        }
        """).RootElement;

    [Fact]
    public void Maps_a_reaction_to_a_candidate()
    {
        var c = BlueskyPosts.ToCandidate(Post("Saw the Clayface movie tonight, the body horror is wild"), MediaType.Movie, Exclude);

        Assert.NotNull(c);
        Assert.Equal("bluesky", c!.Platform);
        Assert.Equal("did:plc:abc123/3kxyz", c.ExternalId);
        Assert.Equal("filmfan.bsky.social", c.Author);
        Assert.Equal("did:plc:abc123", c.AuthorId);
        Assert.Equal(42, c.Upvotes);
        Assert.Equal(7, c.Comments);
        Assert.Equal(new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc), c.CreatedAt);
        Assert.Equal("https://bsky.app/profile/filmfan.bsky.social/post/3kxyz", c.SourceUrl);
        Assert.Null(c.Media);
        Assert.False(c.Sensitive);
    }

    [Fact]
    public void Replies_are_skipped()
        => Assert.Null(BlueskyPosts.ToCandidate(
            Post("this movie was great", extraRecord: """, "reply": { "root": {}, "parent": {} }"""), MediaType.Movie, Exclude));

    [Theory]
    [InlineData("Clayface tonight, wow", MediaType.Movie)]          // no movie/film word, same as X's query
    [InlineData("Watched the Clayface film", MediaType.Tv)]          // a TV title needs show/series/season/episode
    [InlineData("Clayface tickets on sale for the movie", MediaType.Movie)] // promo phrase
    public void Posts_outside_the_rules_are_skipped(string text, MediaType type)
        => Assert.Null(BlueskyPosts.ToCandidate(Post(text), type, Exclude));

    [Fact]
    public void Images_inside_a_quote_with_media_are_kept_and_labels_mark_it_sensitive()
    {
        var c = BlueskyPosts.ToCandidate(Post("my thoughts on the movie",
            extraPost: """
            , "embed": { "$type": "app.bsky.embed.recordWithMedia#view",
                         "media": { "$type": "app.bsky.embed.images#view",
                                    "images": [ { "thumb": "https://cdn.bsky.app/t.jpg", "fullsize": "https://cdn.bsky.app/f.jpg", "alt": "" } ] } },
              "labels": [ { "val": "graphic-media" } ]
            """), MediaType.Movie, Exclude);

        var media = Assert.Single(c!.Media!);
        Assert.Equal("https://cdn.bsky.app/f.jpg", media.Url);
        Assert.Equal("photo", media.Type);
        Assert.Null(media.Alt);
        Assert.True(c.Sensitive);
    }

    [Fact]
    public void A_post_dated_in_the_future_takes_the_time_Bluesky_saw_it()
    {
        var c = BlueskyPosts.ToCandidate(Post("best film of the year",
            extraRecord: "", extraPost: ""), MediaType.Movie, Exclude);
        Assert.NotNull(c);

        var future = JsonDocument.Parse("""
        { "uri": "at://did:plc:abc123/app.bsky.feed.post/3kfut",
          "author": { "did": "did:plc:abc123", "handle": "filmfan.bsky.social" },
          "record": { "text": "best film of the year", "createdAt": "2030-01-01T00:00:00Z" },
          "indexedAt": "2026-10-21T09:30:00Z" }
        """).RootElement;
        Assert.Equal(new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc), BlueskyPosts.ToCandidate(future, MediaType.Movie, Exclude)!.CreatedAt);
    }

    [Theory]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3kxyz", "did:plc:abc123/3kxyz")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.like/3kxyz", null)]   // not a post
    [InlineData("https://bsky.app/profile/x/post/3k", null)]
    [InlineData(null, null)]
    public void External_ids_fit_the_column_and_round_trip(string? uri, string? expected)
    {
        Assert.Equal(expected, BlueskyPosts.ExternalIdFromUri(uri));
        if (expected is not null)
            Assert.Equal(uri, BlueskyPosts.UriFromExternalId(expected));
    }
}
