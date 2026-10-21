using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using Xunit;

namespace SocialMeter.Tests;

public class LexiconSentimentAnalyzerTests
{
    private readonly LexiconSentimentAnalyzer _sut = new();

    [Theory]
    [InlineData("Just watched Dune 2, absolutely incredible! The visuals were stunning and the story was so engaging.", Sentiment.Positive)]
    [InlineData("Honestly disappointed. The pacing felt off and I found myself bored in the middle.", Sentiment.Negative)]
    [InlineData("My movie of the year, just came in the nicest steelbook I've ever seen.", Sentiment.Positive)]
    [InlineData("It's just visually breath taking. So much better than my regular dolby cinema version.", Sentiment.Positive)]
    [InlineData("Genuinely the worst movie I've seen this year and I'm a huge fan of the book", Sentiment.Negative)]
    [InlineData("Hear from the composer at Deadline's Sound & Screen Film event on Friday, November 6", Sentiment.Neutral)]
    [InlineData("Top 10 Highest-Earning Movies of 2026 (so far)", Sentiment.Neutral)]
    [InlineData("Absolute waste of time, fell asleep halfway through", Sentiment.Negative)]
    [InlineData("Solid 8/10, would watch again", Sentiment.Positive)]
    public void Labels_match_the_obvious_reading(string text, Sentiment expected)
    {
        Assert.Equal(expected, _sut.Analyze(text).Label);
    }

    [Fact]
    public void Negation_flips_a_positive_word()
    {
        Assert.True(_sut.Analyze("not good at all").Score < 0);
        Assert.True(_sut.Analyze("not bad honestly").Score > 0);
    }

    [Fact]
    public void Intensifiers_push_further_than_the_bare_word()
    {
        var plain = _sut.Analyze("the ending was good").Score;
        var strong = _sut.Analyze("the ending was absolutely incredible").Score;
        Assert.True(strong > plain);
    }

    [Fact]
    public void Phrases_beat_their_component_words()
    {
        // "waste" alone is negative; "waste of time" is strongly negative and must not be double counted.
        var phrase = _sut.Analyze("complete waste of time").Score;
        Assert.True(phrase <= -0.5, $"expected strongly negative, got {phrase}");
    }

    [Fact]
    public void Ratings_out_of_ten_are_read()
    {
        Assert.True(_sut.Analyze("10/10").Score > 0.5);
        Assert.True(_sut.Analyze("3/10 sadly").Score < -0.3);
    }

    [Fact]
    public void Empty_and_wordless_text_is_neutral()
    {
        Assert.Equal(Sentiment.Neutral, _sut.Analyze("").Label);
        Assert.Equal(Sentiment.Neutral, _sut.Analyze("⣿⣿⣿⣿ 🎬").Label);
    }
}
