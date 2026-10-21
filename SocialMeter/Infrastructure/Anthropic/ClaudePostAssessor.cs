using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
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
           - is about an actor, director, studio, streamer or soundtrack as a person or work apart from this title;
           - for games: is a bug report, crash log, trade/LFG post, or server-status complaint with no view on the game itself.

        2. sentiment — a number from -1.0 (hates the title) to 1.0 (loves it), 0 for neutral or purely informational.
           Score ONLY the feeling toward the title itself. "Grok sucks, this is The Haunting of Hill House" says nothing
           about the show: about_title false, sentiment 0. Sarcasm, slang ("mid", "peak", "goated") and backhanded praise
           count as the feeling they actually convey. When about_title is false, sentiment must be 0.

        Keep each note under 12 words. Judge from the text alone; do not guess what links contain.
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
                    required = new[] { "id", "about_title", "sentiment", "note" },
                    properties = new
                    {
                        id = new { type = "string" },
                        about_title = new { type = "boolean" },
                        sentiment = new { type = "number" },
                        note = new { type = "string" }
                    }
                }
            }
        })
    };

    private readonly AnthropicClient _client;
    private readonly LexiconPostAssessorFallback _fallback;
    private readonly AnthropicOptions _options;
    private readonly ILogger<ClaudePostAssessor> _logger;

    public ClaudePostAssessor(AnthropicClient client, LexiconPostAssessorFallback fallback, IOptions<AnthropicOptions> options, ILogger<ClaudePostAssessor> logger)
    {
        _client = client;
        _fallback = fallback;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "claude";

    public async Task<IReadOnlyList<PostAssessment>> AssessAsync(Title title, IReadOnlyList<SocialPostCandidate> posts, CancellationToken ct = default)
    {
        var results = new PostAssessment[posts.Count];
        var batchSize = Math.Clamp(_options.BatchSize, 1, 50);

        for (var start = 0; start < posts.Count; start += batchSize)
        {
            var batch = posts.Skip(start).Take(batchSize).ToList();
            IReadOnlyList<PostAssessment> assessed;
            try
            {
                assessed = await AssessBatchAsync(title, batch, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Claude assessment failed for {Count} posts about {Title}; using the lexicon for this batch", batch.Count, title.Name);
                assessed = await _fallback.Inner.AssessAsync(title, batch, ct);
            }

            for (var i = 0; i < batch.Count; i++)
                results[start + i] = assessed[i];
        }

        return results;
    }

    private async Task<IReadOnlyList<PostAssessment>> AssessBatchAsync(Title title, IReadOnlyList<SocialPostCandidate> batch, CancellationToken ct)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = 4096,
            System = SystemPrompt,
            Messages = [new UserMessageParam(BuildRequest(title, batch))],
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = Schema }
            }
        }, ct);

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
            results.Add(new PostAssessment(a.AboutTitle, sentiment, a.Note));
        }

        _logger.LogInformation("Claude assessed {Count} posts about {Title}: {OffTopic} off-topic ({In}+{Out} tokens)",
            batch.Count, title.Name, results.Count(r => !r.AboutTitle), response.Usage.InputTokens, response.Usage.OutputTokens);
        return results;
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
            sb.Append("Synopsis: ").AppendLine(Truncate(title.Overview, 300));

        sb.AppendLine().AppendLine("Posts (use the id exactly as given):");
        for (var i = 0; i < batch.Count; i++)
        {
            var p = batch[i];
            sb.Append("[id=").Append(i).Append("] ")
              .Append(p.Platform == "x" ? "X @" : "Reddit u/").Append(p.Author);
            if (p.Community is not null) sb.Append(" in r/").Append(p.Community);
            sb.AppendLine(":");
            sb.AppendLine(Truncate(p.Content, _options.MaxPostChars)).AppendLine();
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class AssessmentResponse
    {
        [JsonPropertyName("assessments")] public List<AssessmentItem> Assessments { get; set; } = new();
    }

    private sealed class AssessmentItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("about_title")] public bool AboutTitle { get; set; }
        [JsonPropertyName("sentiment")] public double Sentiment { get; set; }
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
