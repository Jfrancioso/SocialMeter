using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Guardrails;
using SocialMeter.Application.Social;
using SocialMeter.Application.Tracking;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Infrastructure.Steam;
using SocialMeter.Options;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SocialMeter.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Keeps every log line so a test can check what was written.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Lines.Add((logLevel, formatter(state, exception)));
}

public class UsageMeterTests
{
    private static (UsageMeter Meter, TestClock Clock) Make(GuardrailOptions? o = null)
    {
        var clock = new TestClock();
        return (new UsageMeter(MsOptions.Create(o ?? new GuardrailOptions()), clock), clock);
    }

    [Fact]
    public void Prices_claude_tokens_and_x_reads()
    {
        var (meter, _) = Make();
        meter.RecordClaude(1_000_000, 100_000);   // $4 in + $2 out
        meter.RecordX(100, 20);                   // $0.50 posts + $0.20 profiles

        Assert.Equal(6.70m, meter.EstimatedUsd(meter.Current));
    }

    [Fact]
    public void A_new_utc_day_starts_from_zero_and_keeps_yesterday_for_saving()
    {
        var (meter, clock) = Make();
        meter.RecordLivePull();
        meter.RecordLivePull();

        clock.Now = clock.Now.AddHours(7);        // past midnight UTC
        Assert.Equal(0, meter.Current.LivePulls);

        var unsaved = meter.TakeUnsaved();
        Assert.Single(unsaved);
        Assert.Equal(new DateOnly(2026, 10, 23), unsaved[0].Day);
        Assert.Equal(2, unsaved[0].LivePulls);
    }

    [Fact]
    public void A_restart_picks_up_where_today_left_off()
    {
        var (meter, _) = Make();
        meter.RecordLivePull();
        meter.Restore(new UsageTotals(new DateOnly(2026, 10, 23), 40, 500, 50, 10, 5));
        meter.Restore(new UsageTotals(new DateOnly(2026, 10, 22), 99, 0, 0, 0, 0));   // another day: ignored

        Assert.Equal(41, meter.Current.LivePulls);
        Assert.Equal(10, meter.Current.XPostsRead);
    }

    [Fact]
    public void Only_changed_days_are_handed_to_the_saver_and_a_failed_save_is_retried()
    {
        var (meter, _) = Make();
        Assert.Empty(meter.TakeUnsaved());

        meter.RecordX(3, 0);
        var first = meter.TakeUnsaved();
        Assert.Single(first);
        Assert.Empty(meter.TakeUnsaved());

        meter.MarkUnsaved(first[0]);
        Assert.Single(meter.TakeUnsaved());
    }
}

public class LivePullBudgetTests
{
    private static (LivePullBudget Budget, UsageMeter Meter) Make(GuardrailOptions o)
    {
        var clock = new TestClock();
        var meter = new UsageMeter(MsOptions.Create(o), clock);
        return (new LivePullBudget(MsOptions.Create(o), meter, new GuardrailLog(NullLogger<GuardrailLog>.Instance, clock)), meter);
    }

    [Fact]
    public void The_kill_switch_refuses_everything()
    {
        var (budget, meter) = Make(new GuardrailOptions { LivePullsEnabled = false });
        Assert.Equal(PullRefusal.KillSwitch, budget.TryAcquirePull());
        Assert.Equal(0, meter.Current.LivePulls);
    }

    [Fact]
    public void Reaching_the_daily_spend_limit_turns_on_safe_mode()
    {
        var (budget, meter) = Make(new GuardrailOptions { DailySpendLimitUsd = 1m });
        Assert.Equal(PullRefusal.None, budget.TryAcquirePull());

        meter.RecordClaude(250_000, 0);   // exactly $1
        Assert.Equal(PullRefusal.SpendLimit, budget.TryAcquirePull());
    }

    [Fact]
    public void The_daily_cap_counts_granted_pulls()
    {
        var (budget, meter) = Make(new GuardrailOptions { MaxLivePullsPerDay = 2, MaxLivePullsPerHour = 100 });
        Assert.Equal(PullRefusal.None, budget.TryAcquirePull());
        Assert.Equal(PullRefusal.None, budget.TryAcquirePull());
        Assert.Equal(PullRefusal.DailyCap, budget.TryAcquirePull());
        Assert.Equal(2, meter.Current.LivePulls);
    }

    [Fact]
    public void The_hourly_bucket_still_applies_under_the_daily_cap()
    {
        var (budget, _) = Make(new GuardrailOptions { MaxLivePullsPerHour = 1, MaxLivePullsPerDay = 100 });
        Assert.Equal(PullRefusal.None, budget.TryAcquirePull());
        Assert.Equal(PullRefusal.HourlyCap, budget.TryAcquirePull());
    }

    [Fact]
    public void Tells_readers_when_live_reads_come_back()
    {
        var now = new DateTime(2026, 10, 23, 18, 30, 0, DateTimeKind.Utc);
        Assert.Equal(now + LivePullBudget.RefillEvery, LivePullBudget.RetryAfter(PullRefusal.HourlyCap, now));
        Assert.Equal(new DateTime(2026, 10, 24), LivePullBudget.RetryAfter(PullRefusal.SpendLimit, now));
        Assert.Null(LivePullBudget.RetryAfter(PullRefusal.KillSwitch, now));
    }
}

public class SourceBreakerTests
{
    private static (SourceBreakers Breakers, TestClock Clock, ListLogger<GuardrailLog> Log) Make()
    {
        var clock = new TestClock();
        var log = new ListLogger<GuardrailLog>();
        var options = MsOptions.Create(new GuardrailOptions { BreakerFailures = 3, BreakerMinutes = 15, BreakerHardStopMinutes = 60 });
        return (new SourceBreakers(options, new GuardrailLog(log, clock), clock), clock, log);
    }

    [Fact]
    public void Opens_after_repeated_failures_and_lets_a_trial_through_after_the_cool_down()
    {
        var (breakers, clock, _) = Make();
        breakers.Failed("bluesky", new TimeoutException());
        breakers.Failed("bluesky", new TimeoutException());
        Assert.True(breakers.Allows("bluesky"));

        breakers.Failed("bluesky", new TimeoutException());
        Assert.False(breakers.Allows("bluesky"));

        clock.Now = clock.Now.AddMinutes(15);
        Assert.True(breakers.Allows("bluesky"));
    }

    [Fact]
    public void Out_of_credit_stops_calls_at_once_and_says_so_in_the_log()
    {
        var (breakers, _, log) = Make();
        breakers.Failed("x", new HttpRequestException("credits depleted", null, HttpStatusCode.PaymentRequired));

        Assert.False(breakers.Allows("x"));
        Assert.True(breakers.Allows("bluesky"));   // one service's trouble doesn't touch the others
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.StartsWith("Guardrail tripped: breaker:x.", line.Message);
        Assert.Contains("out of credit (402)", line.Message);
    }

    [Fact]
    public void A_success_closes_the_breaker_and_resets_the_count()
    {
        var (breakers, clock, _) = Make();
        breakers.Failed("x", new HttpRequestException("slow down", null, HttpStatusCode.TooManyRequests));
        clock.Now = clock.Now.AddMinutes(60);
        breakers.Succeeded("x");

        var x = breakers.Snapshot().Single(b => b.Service == "x");
        Assert.False(x.Open);
        Assert.Equal(0, x.FailuresInARow);
        Assert.Equal("rate limited (429)", x.LastError);
    }

    [Fact]
    public void Each_guardrail_is_logged_at_most_once_an_hour()
    {
        var clock = new TestClock();
        var log = new ListLogger<GuardrailLog>();
        var guardrails = new GuardrailLog(log, clock);

        guardrails.Tripped(Guardrail.DailyCap, "first");
        guardrails.Tripped(Guardrail.DailyCap, "again");
        guardrails.Tripped(Guardrail.SpendLimit, "different guardrail");
        clock.Now = clock.Now.AddHours(1);
        guardrails.Tripped(Guardrail.DailyCap, "an hour later");

        Assert.Equal(3, log.Lines.Count);
    }

    [Fact]
    public void The_hourly_cap_is_logged_but_not_as_a_warning()
    {
        var log = new ListLogger<GuardrailLog>();
        new GuardrailLog(log, new TestClock()).Tripped(Guardrail.HourlyCap, "busy");
        Assert.Equal(LogLevel.Information, Assert.Single(log.Lines).Level);
    }
}

public class GuardedIngestionTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Title Buddy = new() { TmdbId = 1, MediaType = MediaType.Movie, Name = "Buddy" };

    private sealed class FakeSource : ISocialSource
    {
        public Func<IReadOnlyList<SocialPostCandidate>> Answer { get; set; } = () => Array.Empty<SocialPostCandidate>();
        public int Calls { get; private set; }
        public string Platform => "x";
        public bool IsConfigured => true;
        public Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default) { Calls++; return Task.FromResult(Answer()); }
        public Task<IReadOnlySet<string>> FindRemovedAsync(IReadOnlyList<string> externalIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    /// <summary>Stands in for Claude: reads every post as positive, and can say the lexicon read them instead.</summary>
    private sealed class FakeClaude : IPostAssessor
    {
        public string? ReadBy { get; set; }
        public int Calls { get; private set; }
        public string Name => "claude";
        public Task<IReadOnlyList<PostAssessment>> AssessAsync(Title title, IReadOnlyList<SocialPostCandidate> posts, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PostAssessment>>(posts.Select(_ => new PostAssessment(true, 0.8, null, ReadBy: ReadBy)).ToList());
        }
    }

    private static SocialPostCandidate Post(string id) => new(
        "x", id, "fan", "Buddy is the creepiest kids show parody I have seen, loved every minute", 10, 2, Now.AddHours(-1), null, null,
        AuthorId: "a" + id, AuthorCreatedAt: Now.AddYears(-3), AuthorReputation: 500);

    private static (PostIngestionService Ingestion, SocialMeterDbContext Db, SourceBreakers Breakers) Make(IPostAssessor assessor)
    {
        var db = new SocialMeterDbContext(new DbContextOptionsBuilder<SocialMeterDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new TestClock();
        var breakers = new SourceBreakers(MsOptions.Create(new GuardrailOptions()), new GuardrailLog(NullLogger<GuardrailLog>.Instance, clock), clock);
        var ingestion = new PostIngestionService(db, assessor, new VerdictAssessor(new LexiconSentimentAnalyzer()),
            new TrustPolicy(MsOptions.Create(new SocialMeterOptions())), breakers, NullLogger<PostIngestionService>.Instance);
        return (ingestion, db, breakers);
    }

    [Fact]
    public async Task A_source_that_is_out_of_credit_is_skipped_on_the_next_pull()
    {
        var (ingestion, _, _) = Make(new FakeClaude());
        var x = new FakeSource { Answer = () => throw new HttpRequestException("credits depleted", null, HttpStatusCode.PaymentRequired) };

        var first = await ingestion.IngestAllAsync(new[] { x }, Buddy, Now, CancellationToken.None);
        var second = await ingestion.IngestAllAsync(new[] { x }, Buddy, Now, CancellationToken.None);

        Assert.Equal(1, x.Calls);                                  // the second pull never called X
        Assert.Equal(PostIngestionService.PublicSourceError, first[0].Error);
        Assert.Equal(PostIngestionService.PublicSourceError, second[0].Error);
    }

    [Fact]
    public async Task Posts_the_fallback_read_are_stamped_as_such_and_re_read_later()
    {
        var claude = new FakeClaude { ReadBy = "lexicon" };      // Claude was down: its fallback read the post
        var (ingestion, db, _) = Make(claude);
        var x = new FakeSource { Answer = () => new[] { Post("1") } };

        await ingestion.IngestAllAsync(new[] { x }, Buddy, Now, CancellationToken.None);
        Assert.Equal("lexicon", db.SocialPosts.Single().Analyzer);

        claude.ReadBy = null;                                     // Claude is back
        await ingestion.IngestAllAsync(new[] { x }, Buddy, Now, CancellationToken.None);
        Assert.Equal(2, claude.Calls);                            // the same post was read again, by Claude this time
        Assert.Equal("claude", db.SocialPosts.Single().Analyzer);
    }
}

public class SteamReviewSourceTests
{
    private sealed class Pages : HttpMessageHandler
    {
        private readonly Queue<string> _bodies;
        public Pages(params string[] bodies) => _bodies = new Queue<string>(bodies);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_bodies.Dequeue(), Encoding.UTF8) });
    }

    private const string BusyPage = "<!DOCTYPE html><html><body>Sorry! An error was encountered</body></html>";
    private const string OneReview = """
        {"success":1,"cursor":"next","reviews":[{"recommendationid":"r1","review":"Great co-op shooter, 40 hours in",
         "author":{"steamid":"7656","num_games_owned":50,"playtime_forever":2400},"timestamp_created":1760000000,
         "voted_up":true,"votes_up":3,"comment_count":0,"steam_purchase":true,"received_for_free":false}]}
        """;

    private static readonly Title Game = new() { TmdbId = 1422450, MediaType = MediaType.Game, Name = "Deadlock" };

    [Fact]
    public async Task A_web_page_instead_of_reviews_is_reported_as_unavailable()
    {
        var source = new SteamReviewSource(new HttpClient(new Pages(BusyPage)), NullLogger<SteamReviewSource>.Instance);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync(Game));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task A_failed_second_page_keeps_the_first()
    {
        var source = new SteamReviewSource(new HttpClient(new Pages(OneReview, BusyPage)), NullLogger<SteamReviewSource>.Instance);
        var reviews = await source.FetchAsync(Game);
        Assert.Equal("r1", Assert.Single(reviews).ExternalId);
    }
}

public class WatchWindowTests
{
    [Fact]
    public void Windows_bind_from_app_settings_in_utc()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Watch:Windows:0:MediaType"] = "movie",
            ["Watch:Windows:0:TmdbId"] = "42",
            ["Watch:Windows:0:Label"] = "Watch party tonight",
            ["Watch:Windows:0:StartsAt"] = "2026-10-24T01:00:00Z",
            ["Watch:Windows:0:EndsAt"] = "2026-10-24T04:00:00Z",
            ["Watch:Windows:0:EveryMinutes"] = "30"
        }).Build();
        var window = config.GetSection(WatchOptions.SectionName).Get<WatchOptions>()!.Windows.Single();

        Assert.Equal(MediaType.Movie, window.MediaType);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero), window.StartsAt);
        Assert.False(window.IsOpen(new DateTimeOffset(2026, 10, 24, 0, 59, 0, TimeSpan.Zero)));
        Assert.True(window.IsOpen(new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero)));
        Assert.False(window.IsOpen(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Only_the_open_window_for_that_title_counts()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero) };
        var options = new WatchOptions
        {
            Windows =
            {
                new WatchWindow { MediaType = MediaType.Movie, TmdbId = 42, Label = "Watch party tonight",
                    StartsAt = clock.Now.AddHours(-1), EndsAt = clock.Now.AddHours(2) },
                new WatchWindow { MediaType = MediaType.Tv, TmdbId = 42, StartsAt = clock.Now.AddDays(1), EndsAt = clock.Now.AddDays(2) }
            }
        };
        var windows = new WatchWindows(new TestOptionsMonitor<WatchOptions>(options), clock);

        Assert.Equal("Watch party tonight", WatchWindows.ReasonFor(windows.OpenFor(MediaType.Movie, 42)!));
        Assert.Null(windows.OpenFor(MediaType.Tv, 42));          // same id, other type, and not open yet
        Assert.Null(windows.OpenFor(MediaType.Movie, 43));
    }

}
