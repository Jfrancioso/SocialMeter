using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface ISentimentAnalyzer
{
    SentimentResult Analyze(string text);
}
