using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Anthropic;
using SocialMeter.Options;
using Xunit;
using Xunit.Abstractions;

namespace SocialMeter.Tests.Evals;

/// <summary>
/// Runs the Claude assessor over the hand-labelled cases in cases.json and asserts a pass rate.
/// Costs real tokens (a few cents per run) and needs Anthropic:ApiKey from the API project's user secrets
/// or the ANTHROPIC_API_KEY environment variable. Excluded from the default run:
///   dotnet test --filter Category=Eval          (run it)
///   dotnet test --filter Category!=Eval         (everything else)
/// Each run writes eval-report.md next to the test assembly.
/// </summary>
[Trait("Category", "Eval")]
public class ClaudeAssessorEval
{
    private const double MinPassRate = 0.85;
    private const double MinRelevanceAccuracy = 0.90;
    private const double Tolerance = 0.15; // slack around each expected range

    private readonly ITestOutputHelper _out;

    public ClaudeAssessorEval(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Assessor_meets_the_pass_rate_on_labelled_cases()
    {
        var apiKey = ResolveApiKey();
        Assert.False(string.IsNullOrWhiteSpace(apiKey),
            "No Anthropic key: set ANTHROPIC_API_KEY or `dotnet user-secrets set Anthropic:ApiKey` in the API project.");

        var cases = JsonSerializer.Deserialize<List<EvalCase>>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Evals", "cases.json")))!;
        var options = Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { ApiKey = apiKey!, IncludeImages = false });
        var assessor = new ClaudePostAssessor(
            new AnthropicClient { ApiKey = apiKey },
            new LexiconPostAssessorFallback(new LexiconSentimentAnalyzer()),
            options,
            NullLogger<ClaudePostAssessor>.Instance);

        var results = new List<CaseResult>();
        foreach (var group in cases.GroupBy(c => (c.Title, c.MediaType, c.Year)))
        {
            var title = new Title
            {
                TmdbId = 0,
                MediaType = Enum.Parse<MediaType>(group.Key.MediaType, true),
                Name = group.Key.Title,
                ReleaseDate = new DateOnly(group.Key.Year, 1, 1)
            };
            var batch = group.ToList();
            var candidates = batch.Select(c => new SocialPostCandidate(c.Platform, c.Id, "author", c.Content, 1, 0, DateTime.UtcNow, null, null)).ToList();
            var assessed = await assessor.AssessAsync(title, candidates);

            for (var i = 0; i < batch.Count; i++)
                results.Add(Grade(batch[i], assessed[i]));
        }

        var report = BuildReport(results);
        _out.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "eval-report.md"), report);

        var passRate = results.Count(r => r.Pass) / (double)results.Count;
        var relevance = results.Count(r => r.RelevanceOk) / (double)results.Count;

        Assert.True(relevance >= MinRelevanceAccuracy, $"relevance accuracy {relevance:P0} < {MinRelevanceAccuracy:P0}\n{report}");
        Assert.True(passRate >= MinPassRate, $"pass rate {passRate:P0} < {MinPassRate:P0}\n{report}");
    }

    private static CaseResult Grade(EvalCase c, PostAssessment a)
    {
        // Lenient cases are genuinely borderline (a renewal reaction, "I want to play it"): either relevance call is
        // accepted, and the ranges only apply if the assessor kept the post.
        var relevanceOk = c.RelevanceLenient || a.AboutTitle == c.AboutTitle;
        var rangesApply = c.AboutTitle && a.AboutTitle;
        var sentimentOk = !rangesApply || (c.Sentiment is { Length: 2 } s && a.Sentiment >= s[0] - Tolerance && a.Sentiment <= s[1] + Tolerance);
        var substanceOk = !rangesApply || (c.Substance is { Length: 2 } b && a.Substance >= b[0] - Tolerance && a.Substance <= b[1] + Tolerance);
        return new CaseResult(c, a, relevanceOk, sentimentOk, substanceOk, relevanceOk && sentimentOk && substanceOk);
    }

    private static string BuildReport(List<CaseResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Assessor eval — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine($"Cases: {results.Count} · Pass: {results.Count(r => r.Pass)} ({results.Count(r => r.Pass) / (double)results.Count:P0}) · " +
                      $"Relevance: {results.Count(r => r.RelevanceOk) / (double)results.Count:P0} · " +
                      $"Sentiment: {results.Count(r => r.SentimentOk) / (double)results.Count:P0} · " +
                      $"Substance: {results.Count(r => r.SubstanceOk) / (double)results.Count:P0}");
        sb.AppendLine();

        foreach (var platform in results.GroupBy(r => r.Case.Platform))
            sb.AppendLine($"- {platform.Key}: {platform.Count(r => r.Pass)}/{platform.Count()} pass");
        sb.AppendLine();

        sb.AppendLine("| case | about | sentiment | substance | note | result |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            var about = r.RelevanceOk ? (r.Assessment.AboutTitle ? "yes" : "no") + (r.Case.RelevanceLenient ? " (lenient)" : "") : $"**{(r.Assessment.AboutTitle ? "yes" : "no")} (exp {(r.Case.AboutTitle ? "yes" : "no")})**";
            var ranges = r.Case.AboutTitle && r.Assessment.AboutTitle;
            var sent = !ranges ? "—" : $"{r.Assessment.Sentiment:+0.00;-0.00} [{r.Case.Sentiment![0]:0.0}..{r.Case.Sentiment[1]:0.0}]" + (r.SentimentOk ? "" : " ❌");
            var subs = !ranges ? "—" : $"{r.Assessment.Substance:0.00} [{r.Case.Substance![0]:0.0}..{r.Case.Substance[1]:0.0}]" + (r.SubstanceOk ? "" : " ❌");
            sb.AppendLine($"| {r.Case.Id} | {about} | {sent} | {subs} | {r.Assessment.Note} | {(r.Pass ? "pass" : "FAIL")} |");
        }
        return sb.ToString();
    }

    private static string? ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        // The API project's user secrets (same id as in SocialMeter.csproj).
        var config = new ConfigurationBuilder().AddUserSecrets("c332e1fc-d898-4ab8-8189-d0d938cbfc22", reloadOnChange: false).Build();
        return config["Anthropic:ApiKey"];
    }

    private sealed record CaseResult(EvalCase Case, PostAssessment Assessment, bool RelevanceOk, bool SentimentOk, bool SubstanceOk, bool Pass);

    private sealed class EvalCase
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("platform")] public string Platform { get; set; } = "x";
        [JsonPropertyName("mediaType")] public string MediaType { get; set; } = "movie";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("year")] public int Year { get; set; }
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("aboutTitle")] public bool AboutTitle { get; set; }
        [JsonPropertyName("relevanceLenient")] public bool RelevanceLenient { get; set; }
        [JsonPropertyName("sentiment")] public double[]? Sentiment { get; set; }
        [JsonPropertyName("substance")] public double[]? Substance { get; set; }
    }
}
