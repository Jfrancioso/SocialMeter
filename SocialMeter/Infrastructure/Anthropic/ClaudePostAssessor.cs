using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Common;
using SocialMeter.Application.Guardrails;
using SocialMeter.Domain.Models;
using SocialMeter.Options;
using MediaType = SocialMeter.Domain.Models.MediaType;

namespace SocialMeter.Infrastructure.Anthropic;

/// <summary>
/// Asks Claude to read each post in the context of the title: is it really a reaction to this movie or show,
/// and how does the author feel about the title itself (not about Grok, the weather, or another film mentioned)?
/// Falls back to the lexicon for a batch when the model call fails, so a key problem never blocks ingestion.
/// </summary>
public sealed class ClaudePostAssessor : IPostAssessor
{
    private const string SystemPrompt = """
        You assess social media posts for SocialMeter, a site that measures how audiences feel about a specific movie,
        TV show or video game. For every post you are given, answer two questions about the ONE title named in the request:

        1. about_title — true only if the post expresses the author's own reaction to, opinion of, or experience with THIS title
           (watched or played it, loved it, hated it, found it boring, recommends it, etc.). It is false when the post:
           - is about a different work with a similar name, or merely mentions the title in passing;
           - is a reply or comment in a thread about something else (identifying a clip, arguing with a bot, tagging friends);
           - is news, a trailer or patch announcement, box-office, sales or ranking lists, promotion, giveaways, key resellers, or an ad;
           - is a question with no opinion ("has anyone seen it?", "does this run on Steam Deck?");
           - only states that the author watched/played it, or wants to, with no reaction ("just finished it", "gonna start this tonight");
           - is really about something else and name-drops the title along the way (a software update that reminded
             them of an episode, a meme format, a tangent) — a passing reference is not a reaction;
           - is about an actor, director, studio, streamer or soundtrack as a person or work apart from this title;
           - for games: is a bug report, crash log, trade/LFG post, or server-status complaint with no view on the game itself.

        2. sentiment — a number from -1.0 (hates the title) to 1.0 (loves it), 0 for neutral or purely informational.
           Score ONLY the feeling toward the title itself. "Grok sucks, this is The Haunting of Hill House" says nothing
           about the show: about_title false, sentiment 0. Sarcasm, slang ("mid", "peak", "goated") and backhanded praise
           count as the feeling they actually convey. A joke or meme that expresses enjoyment or disappointment with the
           title is still an opinion about it — crude or exaggerated phrasing doesn't make it off-topic.
           When about_title is false, sentiment must be 0.

        Some posts come with their attached image(s), labelled "[image for id=N]". Read any text in the image
        (screenshots of other posts, captions, memes) and consider what it shows — the opinion is often there rather
        than in the post's own words. Do not guess what links contain; a bare URL with no image is just a link.

        3. sensitive_media — true if an attached image should not be shown to a general audience without a tap:
           nudity or sexually suggestive imagery, graphic gore beyond ordinary film stills, or hateful content.
           Ordinary posters, screenshots and horror-movie stills are false. Posts without images are false.

        4. substance — 0.0 to 1.0: how much of the author's actual experience with the title is in the post.
           0.1 = a bare reaction with no content ("10/10", "peak", "trash", "loved it"); 0.3 = an opinion with one
           general reason ("great visuals", "too long"); 0.6 = references to specific scenes, characters, episodes,
           mechanics, systems or comparisons; 0.9+ = a developed argument with several specifics. Length alone is not
           substance — a long rant that says nothing specific is 0.2. When about_title is false, substance is 0.

        Source notes: X and Bluesky posts are short by nature — a concise, specific post can still be 0.6. Reddit posts with structure
        and paragraphs usually carry more. Steam-style mentions of hours played or specific mechanics are substance.

        Keep each note under 12 words.
        """;

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "assessments" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            assessments = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "id", "about_title", "sentiment", "substance", "sensitive_media", "note" },
                    properties = new
                    {
                        id = new { type = "string" },
                        about_title = new { type = "boolean" },
                        sentiment = new { type = "number" },
                        substance = new { type = "number" },
                        sensitive_media = new { type = "boolean" },
                        note = new { type = "string" }
                    }
                }
            }
        })
    };

    /// <summary>This assessor's name in SourceBreakers and on the admin page.</summary>
    public const string BreakerName = "claude";

    private readonly AnthropicClient _client;
    private readonly LexiconPostAssessorFallback _fallback;
    private readonly AnthropicOptions _options;
    private readonly UsageMeter _usage;
    private readonly SourceBreakers _breakers;
    private readonly ILogger<ClaudePostAssessor> _logger;

    public ClaudePostAssessor(AnthropicClient client, LexiconPostAssessorFallback fallback, IOptions<AnthropicOptions> options,
        UsageMeter usage, SourceBreakers breakers, ILogger<ClaudePostAssessor> logger)
    {
        _client = client;
        _fallback = fallback;
        _options = options.Value;
        _usage = usage;
        _breakers = breakers;
        _logger = logger;
    }

    public string Name => "claude";

    public async Task<IReadOnlyList<PostAssessment>> AssessAsync(Title title, IReadOnlyList<SocialPostCandidate> posts, CancellationToken ct = default)
    {
        // Batches are independent requests, so a few run at the same time to stay well inside the API's rate limits.
        // Same batches, same tokens, same cost; the reader waits for the slowest batch instead of all of them in turn.
        return await ParallelBatches.RunAsync(posts, Math.Clamp(_options.BatchSize, 1, 50), Math.Clamp(_options.MaxParallelBatches, 1, 8),
            async batch =>
            {
                // While Claude's breaker is open (out of credit, rate limited, failing) the lexicon reads the batch without a
                // doomed call first. Those posts keep the lexicon's name, so a later rescore can give them to Claude.
                if (!_breakers.Allows(BreakerName))
                    return await FallbackAsync(title, batch, ct);

                try
                {
                    var results = await AssessBatchAsync(title, batch, ct);
                    _breakers.Succeeded(BreakerName);
                    return results;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Claude assessment failed for {Count} posts about {Title}; using the lexicon for this batch", batch.Count, title.Name);
                    _breakers.Failed(BreakerName, Classify(ex));
                    return await FallbackAsync(title, batch, ct);
                }
            }, ct);
    }

    /// <summary>The lexicon's reading, marked as the lexicon's, so these posts are not mistaken for ones Claude read.</summary>
    private async Task<IReadOnlyList<PostAssessment>> FallbackAsync(Title title, IReadOnlyList<SocialPostCandidate> batch, CancellationToken ct)
    {
        var results = await _fallback.Inner.AssessAsync(title, batch, ct);
        return results.Select(r => r with { ReadBy = _fallback.Inner.Name }).ToList();
    }

    /// <summary>The Anthropic SDK's own exception types say what went wrong; the ones that won't fix themselves stop calls at once.</summary>
    private static BreakerFailure Classify(Exception ex) => ex switch
    {
        AnthropicRateLimitException => new("rate limited (429)", HardStop: true),
        AnthropicUnauthorizedException or AnthropicForbiddenException => new("API key rejected", HardStop: true),
        AnthropicApiException api when api.ResponseBody?.Contains("credit balance", StringComparison.OrdinalIgnoreCase) == true
            => new("out of credit", HardStop: true),
        AnthropicApiException api => new($"HTTP {(int)api.StatusCode}", HardStop: false),
        _ => SourceBreakers.Classify(ex)
    };

    private async Task<IReadOnlyList<PostAssessment>> AssessBatchAsync(Title title, IReadOnlyList<SocialPostCandidate> batch, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 4096,
            System = SystemPrompt,
            Messages = [new UserMessageParam(BuildContent(title, batch))],
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = Schema }
            }
        }, ct);

        // Count tokens as soon as they are spent, even if the answer below turns out to be unusable.
        _usage.RecordClaude(response.Usage.InputTokens, response.Usage.OutputTokens);

        if (response.StopReason == "refusal")
            throw new InvalidOperationException($"Claude declined the request ({response.StopDetails?.Category}).");

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var parsed = JsonSerializer.Deserialize<AssessmentResponse>(json)
                     ?? throw new InvalidOperationException("Empty assessment response.");

        var byId = parsed.Assessments.ToDictionary(a => a.Id, a => a);
        var results = new List<PostAssessment>(batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
            if (!byId.TryGetValue(i.ToString(), out var a))
                throw new InvalidOperationException($"Assessment for post {i} missing from the response.");

            var sentiment = a.AboutTitle ? Math.Clamp(a.Sentiment, -1, 1) : 0;
            var substance = a.AboutTitle ? Math.Clamp(a.Substance, 0, 1) : 0;
            results.Add(new PostAssessment(a.AboutTitle, sentiment, a.Note, a.SensitiveMedia, substance));
        }

        _logger.LogInformation("Claude assessed {Count} posts about {Title}: {OffTopic} off-topic ({In}+{Out} tokens)",
            batch.Count, title.Name, results.Count(r => !r.AboutTitle), response.Usage.InputTokens, response.Usage.OutputTokens);
        return results;
    }

    /// <summary>The request text followed by each post's first image (within the per-batch cap), labelled by post id.</summary>
    private List<ContentBlockParam> BuildContent(Title title, IReadOnlyList<SocialPostCandidate> batch)
    {
        var blocks = new List<ContentBlockParam> { new TextBlockParam { Text = BuildRequest(title, batch) } };
        if (!_options.IncludeImages) return blocks;

        var budget = Math.Max(0, _options.MaxImagesPerBatch);
        for (var i = 0; i < batch.Count && budget > 0; i++)
        {
            var image = batch[i].Media?.FirstOrDefault(m => m.Url.Length > 0);
            if (image is null) continue;

            blocks.Add(new TextBlockParam { Text = $"[image for id={i}]" });
            blocks.Add(new ImageBlockParam { Source = new UrlImageSource { Url = image.Url } });
            budget--;
        }
        return blocks;
    }

    private string BuildRequest(Title title, IReadOnlyList<SocialPostCandidate> batch)
    {
        var sb = new StringBuilder();
        sb.Append("Title: ").Append(title.Name);
        if (title.Year is { } y) sb.Append(" (").Append(y).Append(')');
        sb.Append(" — ").Append(title.MediaType.Label());
        if (title.Genres.Count > 0) sb.Append(", ").Append(string.Join('/', title.Genres));
        if (title.Developer is not null) sb.Append(", by ").Append(title.Developer);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(title.Overview))
            sb.Append("Synopsis: ").AppendLine(title.Overview.Ellipsize(300));

        sb.AppendLine().AppendLine("Posts (use the id exactly as given):");
        for (var i = 0; i < batch.Count; i++)
        {
            var p = batch[i];
            sb.Append("[id=").Append(i).Append("] ")
              .Append(p.Platform switch { "x" => "X @", "bluesky" => "Bluesky @", _ => "Reddit u/" }).Append(p.Author);
            if (p.Community is not null) sb.Append(" in r/").Append(p.Community);
            if (p.Media is { Count: > 0 }) sb.Append(" (").Append(p.Media.Count).Append(p.Media.Count == 1 ? " image attached" : " images attached").Append(')');
            sb.AppendLine(":");
            sb.AppendLine(p.Content.Ellipsize(_options.MaxPostChars)).AppendLine();
        }
        return sb.ToString();
    }


    private sealed class AssessmentResponse
    {
        [JsonPropertyName("assessments")] public List<AssessmentItem> Assessments { get; set; } = new();
    }

    private sealed class AssessmentItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("about_title")] public bool AboutTitle { get; set; }
        [JsonPropertyName("sentiment")] public double Sentiment { get; set; }
        [JsonPropertyName("substance")] public double Substance { get; set; }
        [JsonPropertyName("sensitive_media")] public bool SensitiveMedia { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
    }
}

/// <summary>Wrapper so DI can hand the lexicon assessor to the Claude one without the two registrations colliding on IPostAssessor.</summary>
public sealed class LexiconPostAssessorFallback
{
    public LexiconPostAssessorFallback(ISentimentAnalyzer sentiment)
    {
        Inner = new SocialMeter.Application.Social.LexiconPostAssessor(sentiment);
    }

    public IPostAssessor Inner { get; }
}
