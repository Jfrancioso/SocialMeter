using SocialMeter.Application.Home;
using SocialMeter.Domain.Models;
using Xunit;

namespace SocialMeter.Tests;

public class FeaturedPickTests
{
    private static readonly Func<TalkedAbout, bool> AllTracked = _ => true;

    [Fact]
    public void The_most_talked_about_qualifying_title_leads()
    {
        var picks = FeaturedPick.Eligible(new[]
        {
            new TalkedAbout(MediaType.Movie, 1, Posts: 40, Confidence: 0.8),
            new TalkedAbout(MediaType.Tv, 2, Posts: 72, Confidence: 0.9),
            new TalkedAbout(MediaType.Movie, 3, Posts: 55, Confidence: 0.7)
        }, AllTracked, minPosts: 10, minConfidence: 0.6).Select(p => p.TmdbId);

        Assert.Equal(new[] { 2, 3, 1 }, picks);
    }

    [Fact]
    public void Thin_readings_untracked_titles_and_games_never_lead()
    {
        var picks = FeaturedPick.Eligible(new[]
        {
            new TalkedAbout(MediaType.Game, 1, Posts: 500, Confidence: 0.99),   // Steam reviews would always win
            new TalkedAbout(MediaType.Tv, 2, Posts: 13, Confidence: 0.46),      // the Apothecary Diaries case
            new TalkedAbout(MediaType.Movie, 3, Posts: 8, Confidence: 0.9),     // too few posts in the window
            new TalkedAbout(MediaType.Movie, 4, Posts: 90, Confidence: 0.9),    // not tracked
            new TalkedAbout(MediaType.Movie, 5, Posts: 30, Confidence: 0.65)
        }, c => c.TmdbId != 4, minPosts: 10, minConfidence: 0.6).Select(p => p.TmdbId);

        Assert.Equal(new[] { 5 }, picks);
    }

    [Fact]
    public void Nothing_qualifying_means_the_caller_falls_back()
        => Assert.Empty(FeaturedPick.Eligible(Array.Empty<TalkedAbout>(), AllTracked, 10, 0.6));
}
