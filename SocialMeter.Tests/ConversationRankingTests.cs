using SocialMeter.Application.Tracking;
using SocialMeter.Domain.Models;
using Xunit;

namespace SocialMeter.Tests;

public class ConversationRankingTests
{
    private static Title T(int id) => new() { TmdbId = id, MediaType = MediaType.Movie, Name = $"Film {id}" };

    [Fact]
    public void Posts_decide_the_order_and_silent_titles_drop_out()
    {
        // TMDB order: 1 (lookalike, measured, no posts), 2, 3, 4 (never measured), 5
        var candidates = new[] { T(1), T(2), T(3), T(4), T(5) };
        var signals = new Dictionary<int, ConversationSignal>
        {
            [1] = new(RecentPosts: 0, TotalPosts: 0),     // the Resident Dead case
            [2] = new(RecentPosts: 12, TotalPosts: 40),
            [3] = new(RecentPosts: 70, TotalPosts: 72),
            [5] = new(RecentPosts: 0, TotalPosts: 9)      // talked about, but not this week
        };

        var order = ConversationRanking.Rank(candidates, signals, take: 20).Select(t => t.TmdbId);
        Assert.Equal(new[] { 3, 2, 5, 4 }, order);
    }

    [Fact]
    public void Unmeasured_titles_keep_the_providers_order_after_measured_ones()
    {
        var order = ConversationRanking.Rank(new[] { T(9), T(8), T(7) }, new Dictionary<int, ConversationSignal>
        {
            [7] = new(RecentPosts: 3, TotalPosts: 3)
        }, take: 20).Select(t => t.TmdbId);
        Assert.Equal(new[] { 7, 9, 8 }, order);
    }

    [Fact]
    public void Duplicates_across_pages_appear_once_and_take_is_respected()
    {
        var order = ConversationRanking.Rank(new[] { T(1), T(2), T(1), T(3) }, new Dictionary<int, ConversationSignal>(), take: 2)
            .Select(t => t.TmdbId);
        Assert.Equal(new[] { 1, 2 }, order);
    }
}
