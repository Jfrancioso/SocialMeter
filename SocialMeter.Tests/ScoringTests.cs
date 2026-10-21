using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using Xunit;

namespace SocialMeter.Tests;

public class ScoringTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static SocialPost P(double sentiment, int upvotes = 0, double trust = 1, double substance = 0.5, bool excluded = false, int ageDays = 1, string platform = "x") => new()
    {
        Platform = platform, SentimentScore = sentiment, Sentiment = SentimentResult.FromScore(sentiment).Label,
        Upvotes = upvotes, TrustWeight = trust, Substance = substance, Excluded = excluded, CreatedAt = Now.AddDays(-ageDays)
    };

    [Fact]
    public void No_posts_is_no_signal_at_fifty()
    {
        var m = SocialScoreCalculator.Compute(Array.Empty<SocialPost>(), Now, 14);
        Assert.Equal(50, m.Score);
        Assert.Equal("No signal", m.Label);
        Assert.Equal(0, m.Confidence);
    }

    [Fact]
    public void All_positive_is_electric_and_all_negative_is_cold()
    {
        var hot = SocialScoreCalculator.Compute(Enumerable.Range(0, 10).Select(_ => P(0.9)).ToList(), Now, 14);
        var cold = SocialScoreCalculator.Compute(Enumerable.Range(0, 10).Select(_ => P(-0.9)).ToList(), Now, 14);
        Assert.Equal("Electric", hot.Label);
        Assert.Equal("Cold", cold.Label);
    }

    [Fact]
    public void Excluded_posts_never_count_but_are_reported()
    {
        var m = SocialScoreCalculator.Compute(new[] { P(0.9), P(-0.9, excluded: true), P(-0.9, excluded: true) }, Now, 14);
        Assert.Equal(1, m.TotalPosts);
        Assert.Equal(2, m.FilteredPosts);
        Assert.True(m.Score > 80);
    }

    [Fact]
    public void Engagement_is_log_scaled_so_one_viral_post_cannot_swamp_the_rest()
    {
        var viral = P(-0.9, upvotes: 100_000);
        var crowd = Enumerable.Range(0, 20).Select(_ => P(0.8, upvotes: 10)).ToList();
        var m = SocialScoreCalculator.Compute(crowd.Append(viral).ToList(), Now, 14);
        Assert.True(m.Score > 50, $"a single viral post flipped the meter to {m.Score}");
    }

    [Fact]
    public void Substance_outweighs_bare_reactions()
    {
        // Same counts, same engagement: five "10/10" against five detailed negative takes should lean negative.
        var quick = Enumerable.Range(0, 5).Select(_ => P(0.9, substance: 0.1)).ToList();
        var detailed = Enumerable.Range(0, 5).Select(_ => P(-0.8, substance: 0.9)).ToList();
        var m = SocialScoreCalculator.Compute(quick.Concat(detailed).ToList(), Now, 14);
        Assert.True(m.Score < 50, $"expected the detailed takes to win, got {m.Score}");
        Assert.Equal(5, m.DetailedPosts);
    }

    [Fact]
    public void Trust_weight_scales_influence()
    {
        var trusted = P(-0.8, trust: 1);
        var sketchy = Enumerable.Range(0, 3).Select(_ => P(0.8, trust: 0.2)).ToList();
        var m = SocialScoreCalculator.Compute(sketchy.Append(trusted).ToList(), Now, 14);
        Assert.True(m.Score < 50);
    }

    [Fact]
    public void Confidence_grows_with_trust_weighted_volume()
    {
        var few = SocialScoreCalculator.Compute(Enumerable.Range(0, 4).Select(_ => P(0.5)).ToList(), Now, 14);
        var many = SocialScoreCalculator.Compute(Enumerable.Range(0, 40).Select(_ => P(0.5)).ToList(), Now, 14);
        var manyWeak = SocialScoreCalculator.Compute(Enumerable.Range(0, 40).Select(_ => P(0.5, trust: 0.1)).ToList(), Now, 14);
        Assert.Equal("low", few.ConfidenceLabel);
        Assert.Equal("high", many.ConfidenceLabel);
        Assert.Equal("low", manyWeak.ConfidenceLabel);
    }

    [Fact]
    public void Momentum_compares_recent_posts_to_the_whole()
    {
        var old = Enumerable.Range(0, 10).Select(_ => P(-0.6, ageDays: 60)).ToList();
        var recent = Enumerable.Range(0, 5).Select(_ => P(0.8, ageDays: 2)).ToList();
        var m = SocialScoreCalculator.Compute(old.Concat(recent).ToList(), Now, 14);
        Assert.NotNull(m.Momentum);
        Assert.True(m.Momentum > 0);
    }

    [Fact]
    public void Platforms_are_summarised_separately()
    {
        var m = SocialScoreCalculator.Compute(new[] { P(0.9, platform: "steam"), P(0.9, platform: "steam"), P(-0.9, platform: "x") }, Now, 14);
        Assert.Equal(2, m.Platforms.Count);
        Assert.Equal("steam", m.Platforms[0].Platform);
        Assert.True(m.Platforms.Single(p => p.Platform == "x").Score < 50);
    }
}

public class VerdictAssessorTests
{
    private readonly VerdictAssessor _sut = new(new LexiconSentimentAnalyzer());

    private static SocialPostCandidate Review(string text, bool up) =>
        new("steam", "r1", "Steam reviewer", text, 3, 0, DateTime.UtcNow, null, null, VerdictUp: up, EngagementMinutes: 600);

    [Fact]
    public void Thumbs_set_the_direction_even_when_the_text_is_odd()
    {
        Assert.True(_sut.Assess(Review("I really wanna stay at your house", true)).Sentiment > 0);
        Assert.True(_sut.Assess(Review("Fix the energy system or watch your game die slowly.", false)).Sentiment < 0);
    }

    [Fact]
    public void Text_that_agrees_strengthens_and_text_that_disagrees_softens()
    {
        var plain = _sut.Assess(Review("gaem gud", true)).Sentiment;
        var glowing = _sut.Assess(Review("Absolutely incredible, best game I have ever played.", true)).Sentiment;
        var mixed = _sut.Assess(Review("Fun game but the wipes are awful and the cheaters are everywhere.", true)).Sentiment;
        Assert.True(glowing > plain);
        Assert.True(mixed < plain);
        Assert.True(mixed > 0); // the thumbs-up still wins
    }

    [Fact]
    public void Verdict_posts_are_always_about_the_title()
    {
        Assert.True(_sut.Assess(Review("D", true)).AboutTitle);
    }
}

public class SubstanceHeuristicTests
{
    [Theory]
    [InlineData("10/10")]
    [InlineData("peak")]
    [InlineData("gaem gud")]
    [InlineData("<3<3<3")]
    public void Bare_reactions_score_low(string text)
    {
        Assert.True(SubstanceHeuristic.Score(text) < 0.3, $"{text} scored {SubstanceHeuristic.Score(text)}");
    }

    [Fact]
    public void Specific_detailed_reviews_score_high()
    {
        const string text = "10/10 definitely the best graphics I've ever seen and the story is amazing, incredibly detailed world, " +
                            "most of the bugs have been fixed over time since launch, one of the best games I've ever played for sure";
        Assert.True(SubstanceHeuristic.Score(text) >= 0.6, $"scored {SubstanceHeuristic.Score(text)}");
    }

    [Fact]
    public void Length_alone_is_not_enough()
    {
        var rant = string.Join(' ', Enumerable.Repeat("this is so bad honestly just awful trash", 10));
        var specific = "CWIS kills tanks in less than 6 seconds. Tank shoots 10 shots taking over 2 min from the main gun and the CWIS still stands.";
        Assert.True(SubstanceHeuristic.Score(specific) > SubstanceHeuristic.Score(rant) * 0.9);
        Assert.True(SubstanceHeuristic.Score(rant) < 0.75);
    }
}
