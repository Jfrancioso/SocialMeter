namespace SocialMeter.Contracts.Admin;

/// <summary>The admin page's Outbox: drafts waiting for a tap, and what was posted or skipped lately.</summary>
public sealed class OutboxDto
{
    /// <summary>Posting:Enabled. When false nothing is drafted and Post is refused.</summary>
    public bool Enabled { get; init; }

    /// <summary>The brand's Bluesky account is set up, so Post can work.</summary>
    public bool CanPost { get; init; }

    /// <summary>The handle it posts as; null until set up.</summary>
    public string? Account { get; init; }

    public int PostedToday { get; init; }
    public int MaxPerDay { get; init; }

    public IReadOnlyList<OutboxEntryDto> Items { get; init; } = Array.Empty<OutboxEntryDto>();
}

public sealed class OutboxEntryDto
{
    public int Id { get; init; }

    /// <summary>"split", "talked-about" or "event".</summary>
    public string Kind { get; init; } = "";

    /// <summary>"draft", "posted", "skipped" or "expired".</summary>
    public string Status { get; init; } = "";

    public string MediaType { get; init; } = "";
    public int TmdbId { get; init; }
    public string TitleName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? DecidedBy { get; init; }

    /// <summary>The post as it would go out now (drafts, numbers current) or as it went out (posted).</summary>
    public string Text { get; init; } = "";

    /// <summary>The title page it links to.</summary>
    public string Url { get; init; } = "";

    /// <summary>The link card's image.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The Bluesky posts quoted in the replies; null for a side with none.</summary>
    public OutboxQuoteDto? For { get; init; }
    public OutboxQuoteDto? Against { get; init; }

    /// <summary>The posted thread on bsky.app.</summary>
    public string? PostedWebUrl { get; init; }

    /// <summary>Why the last Post attempt failed.</summary>
    public string? Error { get; init; }
}

public sealed class OutboxQuoteDto
{
    public int PostId { get; init; }
    public string Author { get; init; } = "";
    public string Content { get; init; } = "";
    public string? SourceUrl { get; init; }
}

/// <summary>What Post or Skip did: the entry as it stands now, or why it was refused.</summary>
public sealed class OutboxActionResultDto
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public OutboxEntryDto? Entry { get; init; }
}
