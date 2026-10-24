using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SocialMeter.Application.Abstractions;
using SocialMeter.Application.Home;
using SocialMeter.Application.Social;
using SocialMeter.Application.Tracking;
using SocialMeter.Contracts.Admin;
using SocialMeter.Domain.Models;
using SocialMeter.Infrastructure.Data;
using SocialMeter.Options;

namespace SocialMeter.Application.Outbox;

/// <summary>
/// The brand account's Outbox. The site drafts (one a day, plus one per live event); an admin posts or skips each draft.
/// Nothing is ever posted without that tap. Drafts store only which title and which Bluesky posts to quote: the facts
/// are read when the draft is shown and again when it's posted, so a post never goes out with yesterday's numbers.
/// Reads stored data only: drafting never pulls from the sources.
/// </summary>
public sealed class OutboxService
{
    private const int Candidates = 10;
    private const int RecentDays = 14;

    private readonly SocialMeterDbContext _db;
    private readonly PostingOptions _options;
    private readonly MostTalkedAbout _talkedAbout;
    private readonly ICrowdShape _crowd;
    private readonly WatchWindows _watch;
    private readonly ITitleService _titles;
    private readonly IBlueskyPoster _poster;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(SocialMeterDbContext db, IOptions<PostingOptions> options, MostTalkedAbout talkedAbout, ICrowdShape crowd,
        WatchWindows watch, ITitleService titles, IBlueskyPoster poster, ILogger<OutboxService> logger)
    {
        _db = db;
        _options = options.Value;
        _talkedAbout = talkedAbout;
        _crowd = crowd;
        _watch = watch;
        _titles = titles;
        _poster = poster;
        _logger = logger;
    }

    // ---- drafting ----

    /// <summary>The worker's hourly call: expire stale drafts, draft for live events, and make the daily draft when due.</summary>
    public async Task<int> DraftDueAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;
        await ExpireAsync(nowUtc, ct);

        var drafted = 0;
        foreach (var window in _watch.Open())
        {
            var since = window.StartsAt.UtcDateTime;
            var exists = await _db.OutboxItems.AnyAsync(o => o.Kind == OutboxItem.Event && o.MediaType == window.MediaType
                && o.TmdbId == window.TmdbId && o.CreatedAt >= since, ct);
            if (!exists && await DraftAsync(OutboxItem.Event, window.MediaType, window.TmdbId, nowUtc, ct) is not null) drafted++;
        }

        var lastDaily = await _db.OutboxItems.Where(o => o.Kind != OutboxItem.Event)
            .OrderByDescending(o => o.CreatedAt).Select(o => (DateTime?)o.CreatedAt).FirstOrDefaultAsync(ct);
        if (OutboxRules.DailyDue(nowUtc, lastDaily, _options.DraftHourUtc) && await DraftDailyAsync(nowUtc, ct) is not null) drafted++;
        return drafted;
    }

    /// <summary>The admin's "Draft one now": the daily pick, whatever the hour.</summary>
    public Task<OutboxItem?> DraftNowAsync(DateTime nowUtc, CancellationToken ct = default) =>
        _options.Enabled ? DraftDailyAsync(nowUtc, ct) : Task.FromResult<OutboxItem?>(null);

    private async Task<OutboxItem?> DraftDailyAsync(DateTime nowUtc, CancellationToken ct)
    {
        var since = nowUtc.AddDays(-_options.RepeatAfterDays);
        var recent = (await _db.OutboxItems.Where(o => o.CreatedAt >= since).Select(o => new { o.MediaType, o.TmdbId }).ToListAsync(ct))
            .Select(o => (o.MediaType, o.TmdbId)).ToHashSet();

        var ranked = new List<OutboxRules.Candidate>();
        foreach (var t in (await _talkedAbout.GetAsync(ct)).Where(t => t.MediaType != MediaType.Game).Take(Candidates))
        {
            var crowd = await _crowd.GetAsync(t.MediaType, t.TmdbId, ct);
            var (forPost, againstPost) = await PickQuotesAsync(t.MediaType, t.TmdbId, nowUtc, ct);
            ranked.Add(new OutboxRules.Candidate(t.MediaType, t.TmdbId, crowd.Enabled ? crowd.Shape : "",
                (forPost is null ? 0 : 1) + (againstPost is null ? 0 : 1)));
        }

        var pick = OutboxRules.PickDaily(ranked, recent);
        return pick is null ? null : await DraftAsync(OutboxRules.KindFor(pick), pick.MediaType, pick.TmdbId, nowUtc, ct);
    }

    private async Task<OutboxItem?> DraftAsync(string kind, MediaType mediaType, int tmdbId, DateTime nowUtc, CancellationToken ct)
    {
        var title = await DetailsAsync(mediaType, tmdbId, ct);
        if (title is null || string.IsNullOrWhiteSpace(title.Name)) return null;
        if (!await _db.TitleSnapshots.AnyAsync(s => s.MediaType == mediaType && s.TmdbId == tmdbId && s.TotalPosts > 0, ct)) return null;

        var (forPost, againstPost) = await PickQuotesAsync(mediaType, tmdbId, nowUtc, ct);
        var item = new OutboxItem
        {
            Kind = kind,
            MediaType = mediaType,
            TmdbId = tmdbId,
            TitleName = title.Name.Length > 200 ? title.Name[..200] : title.Name,
            ForPostId = forPost?.Id,
            AgainstPostId = againstPost?.Id,
            Status = OutboxItem.Draft,
            CreatedAt = nowUtc
        };
        _db.OutboxItems.Add(item);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outbox: drafted {Kind} for {Title}", kind, item.TitleName);
        return item;
    }

    /// <summary>
    /// The strongest Bluesky post on each side that could be featured as written (the "In their words" rules: established
    /// author, no spoilers, no crude wording, words only). Only Bluesky posts can be quoted on Bluesky.
    /// </summary>
    private async Task<(SocialPost? For, SocialPost? Against)> PickQuotesAsync(MediaType mediaType, int tmdbId, DateTime nowUtc, CancellationToken ct)
    {
        var posts = await _db.SocialPosts
            .Where(p => p.TmdbId == tmdbId && p.MediaType == mediaType && p.Platform == "bluesky" && !p.Excluded && p.Sentiment != Sentiment.Neutral)
            .ToListAsync(ct);
        SocialPost? Side(Sentiment s) => HighlightRules.Pick(posts.Where(p => p.Sentiment == s).Select(p => (p, SocialScoreCalculator.Weight(p, nowUtc))));
        return (Side(Sentiment.Positive), Side(Sentiment.Negative));
    }

    private async Task ExpireAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddHours(-_options.ExpireAfterHours);
        var stale = await _db.OutboxItems.Where(o => o.Status == OutboxItem.Draft && o.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var o in stale) o.Status = OutboxItem.Expired;
        if (stale.Count > 0) await _db.SaveChangesAsync(ct);
    }

    // ---- the admin page ----

    public async Task<OutboxDto> ListAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await ExpireAsync(nowUtc, ct);
        var since = nowUtc.AddDays(-RecentDays);
        var items = await _db.OutboxItems.Where(o => o.CreatedAt >= since)
            .OrderByDescending(o => o.Status == OutboxItem.Draft).ThenByDescending(o => o.CreatedAt).ToListAsync(ct);

        var entries = new List<OutboxEntryDto>();
        foreach (var item in items) entries.Add(await EntryAsync(item, nowUtc, ct));

        return new OutboxDto
        {
            Enabled = _options.Enabled,
            CanPost = _options.Enabled && _poster.IsConfigured,
            Account = _poster.Account,
            PostedToday = await PostedTodayAsync(nowUtc, ct),
            MaxPerDay = _options.MaxPerDay,
            Items = entries
        };
    }

    public async Task<OutboxActionResultDto?> PostAsync(int id, string actor, DateTime nowUtc, CancellationToken ct = default)
    {
        var item = await _db.OutboxItems.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (item is null) return null;

        string? refusal =
            !_options.Enabled ? "Posting is turned off (Posting:Enabled)." :
            !_poster.IsConfigured ? "The brand's Bluesky app password isn't set up yet." :
            item.Status != OutboxItem.Draft ? $"This one is already {item.Status}." :
            await PostedTodayAsync(nowUtc, ct) >= _options.MaxPerDay ? $"Today's limit of {_options.MaxPerDay} posts is reached." :
            null;
        if (refusal is not null) return new OutboxActionResultDto { Ok = false, Error = refusal, Entry = await EntryAsync(item, nowUtc, ct) };

        var composed = await ComposeAsync(item, nowUtc, ct);
        if (composed is null) return new OutboxActionResultDto { Ok = false, Error = "This title has no reading to post.", Entry = await EntryAsync(item, nowUtc, ct) };

        var quotes = new List<BlueskyQuote>();
        if (composed.For?.ExternalId is { } f && Infrastructure.Bluesky.BlueskyPosts.UriFromExternalId(f) is { } forUri) quotes.Add(new BlueskyQuote(OutboxRules.CaseFor, forUri));
        if (composed.Against?.ExternalId is { } a && Infrastructure.Bluesky.BlueskyPosts.UriFromExternalId(a) is { } againstUri) quotes.Add(new BlueskyQuote(OutboxRules.CaseAgainst, againstUri));

        try
        {
            var result = await _poster.PostThreadAsync(new BlueskyThread(composed.Text, OutboxRules.Display(composed.Url),
                new BlueskyLinkCard(composed.Url, composed.CardTitle, composed.CardDescription, composed.ImageUrl), quotes), ct);
            item.Status = OutboxItem.Posted;
            item.PostedText = composed.Text;
            item.PostedUri = result.Uri;
            item.Error = null;
            item.DecidedAt = nowUtc;
            item.DecidedBy = actor;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Outbox: posted {Title} to Bluesky ({Quotes} quotes)", item.TitleName, result.QuotesPosted);
            return new OutboxActionResultDto { Ok = true, Entry = await EntryAsync(item, nowUtc, ct) };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            item.Error = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Outbox: posting {Title} failed ({Error})", item.TitleName, ex.GetType().Name);
            return new OutboxActionResultDto { Ok = false, Error = item.Error, Entry = await EntryAsync(item, nowUtc, ct) };
        }
    }

    public async Task<OutboxActionResultDto?> SkipAsync(int id, string actor, DateTime nowUtc, CancellationToken ct = default)
    {
        var item = await _db.OutboxItems.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (item is null) return null;
        if (item.Status != OutboxItem.Draft)
            return new OutboxActionResultDto { Ok = false, Error = $"This one is already {item.Status}.", Entry = await EntryAsync(item, nowUtc, ct) };

        item.Status = OutboxItem.Skipped;
        item.DecidedAt = nowUtc;
        item.DecidedBy = actor;
        await _db.SaveChangesAsync(ct);
        return new OutboxActionResultDto { Ok = true, Entry = await EntryAsync(item, nowUtc, ct) };
    }

    private Task<int> PostedTodayAsync(DateTime nowUtc, CancellationToken ct)
    {
        var day = nowUtc.Date;
        return _db.OutboxItems.CountAsync(o => o.Status == OutboxItem.Posted && o.DecidedAt >= day, ct);
    }

    // ---- what a post says ----

    private sealed record Composed(string Text, string Url, string CardTitle, string CardDescription, string? ImageUrl, SocialPost? For, SocialPost? Against);

    /// <summary>The post as it would go out now: current numbers, and the quoted posts if they still count.</summary>
    private async Task<Composed?> ComposeAsync(OutboxItem item, DateTime nowUtc, CancellationToken ct)
    {
        var snapshot = await _db.TitleSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MediaType == item.MediaType && s.TmdbId == item.TmdbId, ct);
        if (snapshot is null || snapshot.TotalPosts == 0) return null;

        var title = await DetailsAsync(item.MediaType, item.TmdbId, ct);
        var crowd = await _crowd.GetAsync(item.MediaType, item.TmdbId, ct);
        var live = _watch.OpenFor(item.MediaType, item.TmdbId);
        var url = OutboxRules.TitleUrl(_options.SiteUrl, item.MediaType, item.TmdbId);

        var facts = new OutboxRules.Facts(title?.Name ?? item.TitleName, title?.ReleaseDate?.Year, snapshot.Score, snapshot.Label, snapshot.TotalPosts,
            crowd.Enabled ? crowd.Positive : null, crowd.Enabled ? crowd.Negative : null,
            item.Kind == OutboxItem.Event && live is not null ? WatchWindows.ReasonFor(live) : null);
        var text = OutboxRules.Text(facts, OutboxRules.Display(url));

        var ids = new[] { item.ForPostId, item.AgainstPostId }.OfType<int>().ToList();
        var quoted = await _db.SocialPosts.AsNoTracking().Where(p => ids.Contains(p.Id) && !p.Excluded).ToListAsync(ct);

        var cardTitle = facts.Year is { } y ? $"{facts.Name} ({y}) · Chatterbub score {facts.Score}" : $"{facts.Name} · Chatterbub score {facts.Score}";
        var cardDescription = facts.Loved is { } loved && facts.Hated is { } hated
            ? $"{Math.Round(loved)}% loved it, {Math.Round(hated)}% hated it, from {facts.Posts} posts. See the posts behind the number."
            : $"Scored from {facts.Posts} posts. See the posts behind the number.";

        return new Composed(text, url, cardTitle, cardDescription, ImageUrl(title),
            quoted.FirstOrDefault(p => p.Id == item.ForPostId), quoted.FirstOrDefault(p => p.Id == item.AgainstPostId));
    }

    private async Task<OutboxEntryDto> EntryAsync(OutboxItem item, DateTime nowUtc, CancellationToken ct)
    {
        var composed = item.Status == OutboxItem.Draft ? await ComposeAsync(item, nowUtc, ct) : null;
        var quotedIds = new[] { item.ForPostId, item.AgainstPostId }.OfType<int>().ToList();
        var quoted = composed is null
            ? await _db.SocialPosts.AsNoTracking().Where(p => quotedIds.Contains(p.Id)).ToListAsync(ct)
            : new[] { composed.For, composed.Against }.OfType<SocialPost>().ToList();

        return new OutboxEntryDto
        {
            Id = item.Id,
            Kind = item.Kind,
            Status = item.Status,
            MediaType = item.MediaType.ToString().ToLowerInvariant(),
            TmdbId = item.TmdbId,
            TitleName = item.TitleName,
            CreatedAt = item.CreatedAt,
            DecidedAt = item.DecidedAt,
            DecidedBy = item.DecidedBy,
            Text = item.PostedText ?? composed?.Text ?? "",
            Url = OutboxRules.TitleUrl(_options.SiteUrl, item.MediaType, item.TmdbId),
            ImageUrl = composed?.ImageUrl,
            For = Quote(quoted.FirstOrDefault(p => p.Id == item.ForPostId)),
            Against = Quote(quoted.FirstOrDefault(p => p.Id == item.AgainstPostId)),
            PostedWebUrl = item.PostedUri is null ? null : Infrastructure.Bluesky.BlueskyRecords.WebUrl(item.PostedUri),
            Error = item.Error
        };
    }

    private static OutboxQuoteDto? Quote(SocialPost? p) => p is null ? null : new OutboxQuoteDto
    {
        PostId = p.Id,
        Author = p.Author,
        Content = p.Content.Length > 600 ? p.Content[..600] : p.Content,
        SourceUrl = p.SourceUrl
    };

    /// <summary>
    /// The card image: TMDB's backdrop for movies and shows; for games Steam's header art (small; its hero art can be
    /// over Bluesky's one-megabyte limit, and a game's art fields already hold full Steam addresses).
    /// </summary>
    private static string? ImageUrl(Title? title)
    {
        if (title is null) return null;
        if (title.MediaType == MediaType.Game)
            return !string.IsNullOrEmpty(title.HeaderImage) ? title.HeaderImage : null;
        if (string.IsNullOrEmpty(title.BackdropPath)) return null;
        return title.BackdropPath.StartsWith("http", StringComparison.Ordinal) ? title.BackdropPath : $"https://image.tmdb.org/t/p/w780{title.BackdropPath}";
    }

    private async Task<Title?> DetailsAsync(MediaType mediaType, int tmdbId, CancellationToken ct)
    {
        try
        {
            return await _titles.GetDetailsAsync(mediaType, tmdbId, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning("Outbox: title details unavailable for {Type} {Id} ({Error})", mediaType, tmdbId, ex.GetType().Name);
            return null;
        }
    }
}
