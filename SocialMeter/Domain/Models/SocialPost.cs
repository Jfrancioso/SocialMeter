namespace SocialMeter.Domain.Models;

public class SocialPost
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Sentiment { get; set; } = string.Empty;
    public int Upvotes { get; set; }
    public int Comments { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string? Subreddit { get; set; }
}

public class SocialMeterScore
{
    public int Score { get; set; }
    public string Confidence { get; set; } = string.Empty;
    public int TotalPosts { get; set; }
    public SentimentBreakdown Breakdown { get; set; } = new();
}

public class SentimentBreakdown
{
    public int Positive { get; set; }
    public int Negative { get; set; }
    public int Neutral { get; set; }
}