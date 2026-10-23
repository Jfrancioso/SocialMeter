using Microsoft.Extensions.Options;
using SocialMeter.Options;

namespace SocialMeter.Application.Guardrails;

/// <summary>Paid usage for one UTC day.</summary>
public sealed record UsageTotals(DateOnly Day, int LivePulls, long ClaudeInputTokens, long ClaudeOutputTokens, int XPostsRead, int XUsersRead)
{
    public static UsageTotals Empty(DateOnly day) => new(day, 0, 0, 0, 0, 0);
}

/// <summary>
/// Counts today's paid usage (UTC day) as it happens: live pulls, Claude tokens, X posts and profiles read. The spend
/// guard and the daily cap read it on every pull, so it lives in memory; UsageMeterWorker saves it to the UsageDays table
/// every minute and loads it back at startup, so a deploy or restart doesn't hand out a fresh day's budget.
///
/// In memory rather than one database write per call because Claude batches run in parallel, and a DbContext must never
/// be used from parallel tasks (the 2026-10-23 home-page incident). One lock guards everything; it is held for a few
/// additions at a time. Per process: behind two instances the count would move to the database.
/// </summary>
public sealed class UsageMeter
{
    private readonly object _lock = new();
    private readonly GuardrailOptions _options;
    private readonly TimeProvider _clock;
    private UsageTotals _today;
    private bool _todayDirty;

    /// <summary>Yesterday's last numbers when the day rolled over before they were saved.</summary>
    private UsageTotals? _unsavedYesterday;

    public UsageMeter(IOptions<GuardrailOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
        _today = UsageTotals.Empty(Today());
    }

    public UsageTotals Current
    {
        get { lock (_lock) { RollOver(); return _today; } }
    }

    /// <summary>Today's usage priced at the configured rates. An estimate: the bills themselves are in each provider's console.</summary>
    public decimal EstimatedUsd(UsageTotals t) =>
        t.ClaudeInputTokens * _options.ClaudeInputUsdPerMillion / 1_000_000m
        + t.ClaudeOutputTokens * _options.ClaudeOutputUsdPerMillion / 1_000_000m
        + t.XPostsRead * _options.XUsdPerPost
        + t.XUsersRead * _options.XUsdPerUser;

    public void RecordLivePull() => Add(t => t with { LivePulls = t.LivePulls + 1 });

    public void RecordClaude(long inputTokens, long outputTokens) =>
        Add(t => t with { ClaudeInputTokens = t.ClaudeInputTokens + inputTokens, ClaudeOutputTokens = t.ClaudeOutputTokens + outputTokens });

    public void RecordX(int posts, int users) =>
        Add(t => t with { XPostsRead = t.XPostsRead + posts, XUsersRead = t.XUsersRead + users });

    /// <summary>For the worker: the days whose numbers changed since the last save (yesterday's last minute, today).</summary>
    public IReadOnlyList<UsageTotals> TakeUnsaved()
    {
        lock (_lock)
        {
            RollOver();
            var unsaved = new List<UsageTotals>(2);
            if (_unsavedYesterday is not null) unsaved.Add(_unsavedYesterday);
            if (_todayDirty) unsaved.Add(_today);
            _unsavedYesterday = null;
            _todayDirty = false;
            return unsaved;
        }
    }

    /// <summary>For the worker when a save failed: these numbers still need saving.</summary>
    public void MarkUnsaved(UsageTotals totals)
    {
        lock (_lock)
        {
            RollOver();
            if (totals.Day == _today.Day) _todayDirty = true;
            else _unsavedYesterday ??= totals;
        }
    }

    /// <summary>For the worker at startup: adds what was saved for today before the restart.</summary>
    public void Restore(UsageTotals saved)
    {
        lock (_lock)
        {
            RollOver();
            if (saved.Day != _today.Day) return;
            _today = _today with
            {
                LivePulls = _today.LivePulls + saved.LivePulls,
                ClaudeInputTokens = _today.ClaudeInputTokens + saved.ClaudeInputTokens,
                ClaudeOutputTokens = _today.ClaudeOutputTokens + saved.ClaudeOutputTokens,
                XPostsRead = _today.XPostsRead + saved.XPostsRead,
                XUsersRead = _today.XUsersRead + saved.XUsersRead
            };
        }
    }

    private void Add(Func<UsageTotals, UsageTotals> change)
    {
        lock (_lock)
        {
            RollOver();
            _today = change(_today);
            _todayDirty = true;
        }
    }

    /// <summary>At midnight UTC a new day starts from zero; the old day's numbers are kept until the worker saves them.</summary>
    private void RollOver()
    {
        var day = Today();
        if (day == _today.Day) return;
        if (_todayDirty) _unsavedYesterday = _today;
        _today = UsageTotals.Empty(day);
        _todayDirty = false;
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
}
