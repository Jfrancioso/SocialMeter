using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SocialMeter.Application.Events;
using SocialMeter.Application.Tracking;
using SocialMeter.Infrastructure.Data;
using Xunit;

namespace SocialMeter.Tests;

public class OutboundClicksTests
{
    [Theory]
    [InlineData("post", "x.com", "x.com")]
    [InlineData("rating", "WWW.RottenTomatoes.com", "www.rottentomatoes.com")]
    [InlineData("trailer", " www.youtube.com ", "www.youtube.com")]
    public void Known_kinds_and_plain_hosts_are_kept(string kind, string host, string stored)
    {
        Assert.Equal((kind, stored), OutboundClicks.Normalize(kind, host));
    }

    [Theory]
    [InlineData("ads", "x.com")]                         // not a kind we have
    [InlineData("post", "https://x.com")]                // a URL, not a host
    [InlineData("post", "x.com/some/path")]
    [InlineData("post", "localhost")]                    // no dot
    [InlineData("post", ".x.com")]
    [InlineData("post", "x.com.")]
    [InlineData("post", "<script>.com")]
    [InlineData("post", "")]
    [InlineData(null, "x.com")]
    public void Anything_else_is_dropped(string? kind, string host)
    {
        Assert.Null(OutboundClicks.Normalize(kind, host));
    }

    [Fact]
    public void Very_long_hosts_are_dropped()
    {
        Assert.Null(OutboundClicks.Normalize("post", new string('a', 97) + ".com"));
    }

    [Fact]
    public async Task Clicks_add_up_per_day_kind_and_site()
    {
        var db = new SocialMeterDbContext(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sut = new OutboundClicks(db, NullLogger<OutboundClicks>.Instance);
        var day1 = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);

        await sut.RecordAsync("post", "x.com", day1);
        await sut.RecordAsync("post", "x.com", day1.AddHours(5));
        await sut.RecordAsync("post", "bsky.app", day1);
        await sut.RecordAsync("post", "x.com", day1.AddDays(1));

        var rows = await db.OutboundClicks.OrderBy(r => r.Day).ThenBy(r => r.Host).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Single(r => r.Host == "x.com" && r.Day == DateOnly.FromDateTime(day1)).Count);
    }
}

public class SeasonalThemeTests
{
    [Fact]
    public void October_finds_shows_by_the_horror_keyword_not_the_mystery_genre()
    {
        var october = SeasonalTheme.For(new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("halloween", october.Key);
        Assert.Equal(SeasonalTheme.HorrorKeyword, october.TvKeywordId);
    }

    [Fact]
    public void Other_months_use_genres_only()
    {
        Assert.Null(SeasonalTheme.For(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)).TvKeywordId);
        Assert.Null(SeasonalTheme.For(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)).TvKeywordId);
    }
}
