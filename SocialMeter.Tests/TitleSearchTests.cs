using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Bluesky;
using SocialMeter.Infrastructure.X;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

public class TitleSearchTests
{
    private static readonly Title From = new()
    {
        TmdbId = 124364, MediaType = MediaType.Tv, Name = "FROM",
        Creators = new[] { "John Griffin" },
        Cast = new[]
        {
            new CastCredit("Harold Perrineau", "Boyd Stevens"), new CastCredit("Catalina Sandino Moreno", "Tabitha Matthews"),
            new CastCredit("Ricky He", "Kenny Liu"), new CastCredit("David Alpay", "Jade Herrera"), new CastCredit("Chloe Van Landschoot", "Kristi Miller")
        }
    };

    [Theory]
    [InlineData("FROM", true)]
    [InlineData("Him", true)]
    [InlineData("Us", true)]
    [InlineData("It", true)]
    [InlineData("Smile", true)]
    [InlineData("The Boys", false)]
    [InlineData("Weapons", true)]
    [InlineData("Clayface", false)]
    [InlineData("The Terminal List", false)]
    [InlineData("Dune: Part Two", false)]
    [InlineData("Street Fighter", false)]
    [InlineData("Buddy", false)]
    public void Everyday_word_names_are_ambiguous(string name, bool ambiguous)
        => Assert.Equal(ambiguous, TitleSearchRules.IsAmbiguous(name));

    [Fact]
    public void An_ambiguous_title_is_pinned_down_by_creators_cast_and_characters_with_hand_picked_terms_first()
    {
        var search = TitleSearchRules.For(From, new TitleSearchTuning { Terms = { "Fromville" } });

        Assert.True(search.Ambiguous);
        Assert.False(search.Skip);
        Assert.Equal("Fromville", search.Context[0]);
        Assert.Contains("John Griffin", search.Context);
        Assert.Contains("Harold Perrineau", search.Context);
        Assert.Contains("Boyd Stevens", search.Context);
        Assert.DoesNotContain("Chloe Van Landschoot", search.Context);   // only the top four billed
        Assert.True(search.Context.Count <= TitleSearchRules.MaxContext);
    }

    [Fact]
    public void A_distinctive_title_needs_no_context_and_an_unpinnable_one_is_skipped()
    {
        Assert.Empty(TitleSearchRules.For(new Title { Name = "Clayface", MediaType = MediaType.Movie }, null).Context);
        Assert.True(TitleSearchRules.For(new Title { Name = "Him", MediaType = MediaType.Movie }, null).Skip);
    }

    [Fact]
    public void Configuration_can_force_either_way()
    {
        Assert.False(TitleSearchRules.For(From, new TitleSearchTuning { Ambiguous = false }).Ambiguous);
        Assert.True(TitleSearchRules.For(new Title { Name = "Buddy", MediaType = MediaType.Movie, Creators = new[] { "Casper Kelly" } },
            new TitleSearchTuning { Ambiguous = true }).Ambiguous);
    }

    [Fact]
    public void Per_title_terms_bind_from_app_settings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Search:Titles:tv_124364:Terms:0"] = "Fromville",
            ["Search:Titles:TV_1:Ambiguous"] = "true"
        }).Build();
        var options = config.GetSection(SearchOptions.SectionName).Get<SearchOptions>()!;

        Assert.Equal("Fromville", options.Titles["tv_124364"].Terms.Single());
        Assert.True(options.Titles["tv_1"].Ambiguous);   // keys are case-insensitive
    }

    // ---- X ----

    [Fact]
    public void X_queries_for_distinctive_titles_are_unchanged()
    {
        var search = new TitleSearch("Clayface", false, Array.Empty<string>());
        Assert.Equal("\"Clayface\" (movie OR film) -is:retweet -is:reply -is:nullcast lang:en -\"box office\" -tickets",
            XSource.BuildQuery(search, MediaType.Movie, new[] { "box office", "tickets" }));
    }

    [Fact]
    public void X_queries_for_everyday_words_require_a_context_term_and_fit_the_limit()
    {
        var search = TitleSearchRules.For(From, new TitleSearchTuning { Terms = { "Fromville" } });
        var query = XSource.BuildQuery(search, MediaType.Tv, new[] { "box office" });

        Assert.StartsWith("\"FROM\" (Fromville OR \"John Griffin\" OR \"Harold Perrineau\"", query);
        Assert.DoesNotContain("(show OR series", query);

        var crowded = new TitleSearch("FROM", true, Enumerable.Range(0, 60).Select(i => $"Some Long Character Name {i}").ToList());
        var trimmed = XSource.BuildQuery(crowded, MediaType.Tv, Array.Empty<string>());
        Assert.True(trimmed.Length <= XSource.MaxQueryLength);
        Assert.Contains("\"Some Long Character Name 0\"", trimmed);   // the first, most telling terms survive
    }

    // ---- Bluesky ----

    [Fact]
    public void Bluesky_queries_use_its_or_syntax()
    {
        Assert.Equal("\"Clayface\"", BlueskyPosts.Query(new TitleSearch("Clayface", false, Array.Empty<string>())));
        Assert.Equal("\"FROM\" (Fromville | \"Harold Perrineau\")",
            BlueskyPosts.Query(new TitleSearch("FROM", true, new[] { "Fromville", "Harold Perrineau" })));
    }

    private static JsonElement Post(string text) => JsonDocument.Parse($$"""
        { "uri": "at://did:plc:abc/app.bsky.feed.post/3k", "author": { "did": "did:plc:abc", "handle": "fan.bsky.social" },
          "record": { "text": {{JsonSerializer.Serialize(text)}}, "createdAt": "2026-10-20T18:00:00Z" }, "likeCount": 1, "replyCount": 0 }
        """).RootElement;

    [Fact]
    public void A_post_naming_a_context_term_counts_without_a_media_word()
    {
        var context = new[] { "Harold Perrineau", "Fromville" };

        Assert.NotNull(BlueskyPosts.ToCandidate(Post("Seasons 1-3 of From, starring Harold Perrineau, are on Netflix"), MediaType.Tv, Array.Empty<string>(), context));
        Assert.NotNull(BlueskyPosts.ToCandidate(Post("Watching the show From and yelling at Boyd"), MediaType.Tv, Array.Empty<string>(), context));
        Assert.Null(BlueskyPosts.ToCandidate(Post("Me coming home from kindergarten"), MediaType.Tv, Array.Empty<string>(), context));
        // Without context (a distinctive name) the old rule stands: the post must say it's about a show.
        Assert.Null(BlueskyPosts.ToCandidate(Post("starring Harold Perrineau"), MediaType.Tv, Array.Empty<string>()));
    }
}
