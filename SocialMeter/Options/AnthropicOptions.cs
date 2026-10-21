namespace SocialMeter.Options;

/// <summary>Claude-based post assessment. Without an API key the lexicon assessor is used instead.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = "";

    /// <summary>Model id. Opus 5.5 is the default; claude-haiku-5-5 is ~40x cheaper if the budget matters more than nuance.</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Posts per request. Larger batches cost less per post but risk one bad post derailing the whole answer.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Characters of each post sent to the model.</summary>
    public int MaxPostChars { get; set; } = 700;

    /// <summary>Send attached images so the model can read captions and screenshots. Roughly 1–1.5k tokens per image.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>Upper bound on images per request; posts beyond it are judged on text alone.</summary>
    public int MaxImagesPerBatch { get; set; } = 10;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
