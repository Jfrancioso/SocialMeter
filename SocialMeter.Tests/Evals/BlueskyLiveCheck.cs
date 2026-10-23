using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Bluesky;
using SocialMeter.Options;
using Xunit;
using Xunit.Abstractions;

namespace SocialMeter.Tests.Evals;

/// <summary>
/// Calls the real Bluesky API with the app password in the API project's user secrets. Free: it never touches X or
/// Claude. Excluded from CI with the other evals; run with dotnet test --filter "FullyQualifiedName~BlueskyLiveCheck".
/// </summary>
[Trait("Category", "Eval")]
public class BlueskyLiveCheck
{
    private readonly ITestOutputHelper _out;
    public BlueskyLiveCheck(ITestOutputHelper output) => _out = output;

    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task Fetches_current_reactions_and_rechecks_them()
    {
        var config = new ConfigurationBuilder().AddUserSecrets("c332e1fc-d898-4ab8-8189-d0d938cbfc22", reloadOnChange: false).Build();
        var options = new BlueskyOptions();
        config.GetSection(BlueskyOptions.SectionName).Bind(options);
        if (!options.IsConfigured)
        {
            _out.WriteLine("Bluesky:Identifier / Bluesky:AppPassword not set; skipped.");
            return;
        }

        var opts = Microsoft.Extensions.Options.Options.Create(options);
        var session = new BlueskySession(new Factory(), opts, NullLogger<BlueskySession>.Instance);
        var searches = new TitleSearchPlanner(new TestOptionsMonitor<SearchOptions>(new SearchOptions
        {
            Titles = { ["tv_124364"] = new TitleSearchTuning { Terms = { "Fromville" } } }
        }));
        var source = new BlueskySource(new HttpClient(), session, opts, searches, NullLogger<BlueskySource>.Instance);

        // FROM: an everyday word, so the search carries its context. Printed for a person to judge, not asserted:
        // the show is between seasons and some weeks have very few posts.
        var from = new Title
        {
            TmdbId = 124364, MediaType = MediaType.Tv, Name = "FROM", Creators = new[] { "John Griffin" },
            Cast = new[] { new CastCredit("Harold Perrineau", "Boyd Stevens"), new CastCredit("Catalina Sandino Moreno", "Tabitha Matthews") }
        };
        var fromPosts = await source.FetchAsync(from);
        _out.WriteLine($"FROM: {fromPosts.Count} posts");
        foreach (var p in fromPosts.Take(8))
            _out.WriteLine($"  {p.Upvotes,5} likes  {p.Content.ReplaceLineEndings(" ")[..Math.Min(90, p.Content.Length)]}");

        var title = new Title { TmdbId = 1400940, MediaType = MediaType.Movie, Name = "Clayface", ReleaseDate = new DateOnly(2026, 10, 23) };
        var posts = await source.FetchAsync(title);

        _out.WriteLine($"{posts.Count} posts; {posts.Count(p => p.AuthorCreatedAt is not null)} with account age; {posts.Count(p => p.Media is not null)} with media");
        foreach (var p in posts.Take(8))
            _out.WriteLine($"  {p.Upvotes,5} likes  @{p.Author}  {p.Content.ReplaceLineEndings(" ")[..Math.Min(90, p.Content.Length)]}");

        Assert.NotEmpty(posts);
        Assert.True(posts.Count <= options.MaxResults);
        Assert.All(posts, p => Assert.Equal("bluesky", p.Platform));
        Assert.Contains(posts, p => p.AuthorCreatedAt is not null);

        var removed = await source.FindRemovedAsync(posts.Select(p => p.ExternalId).ToList());
        _out.WriteLine($"recheck: {removed.Count} of {posts.Count} reported removed");
        Assert.True(removed.Count <= 1, "posts fetched a moment ago should still exist");
    }
}
