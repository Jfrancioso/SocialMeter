using Microsoft.EntityFrameworkCore;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;
using Xunit;

namespace SocialMeter.Tests;

public class SeoTests
{
    [Fact]
    public async Task The_sitemap_lists_only_titles_with_a_reading_newest_first()
    {
        var db = new SocialMeterDbContext(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = new DateTime(2026, 10, 23, 20, 0, 0, DateTimeKind.Utc);
        db.TitleSnapshots.AddRange(
            new TitleSnapshot { TmdbId = 1, MediaType = MediaType.Movie, TotalPosts = 40, Label = "Hot", LastRefreshedAt = now.AddHours(-5) },
            new TitleSnapshot { TmdbId = 2, MediaType = MediaType.Tv, TotalPosts = 9, Label = "Hot", LastRefreshedAt = now.AddHours(-1) },
            new TitleSnapshot { TmdbId = 3, MediaType = MediaType.Movie, TotalPosts = 0, Label = "No signal", LastRefreshedAt = now });   // nothing counted
        await db.SaveChangesAsync();

        var options = Microsoft.Extensions.Options.Options.Create(new SocialMeterOptions());
        var queries = new PostQueryService(db, new ScoreHistoryService(db, new PullGate(), options));
        var titles = await queries.GetScoredTitlesAsync(10);

        Assert.Equal(new[] { (MediaType.Tv, 2), (MediaType.Movie, 1) }, titles.Select(t => (t.Type, t.Id)));
        Assert.Equal(now.AddHours(-1), titles[0].LastModified);
    }
}
