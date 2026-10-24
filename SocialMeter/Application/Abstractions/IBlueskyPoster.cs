namespace SocialMeter.Application.Abstractions;

/// <summary>
/// Posts as the brand's Bluesky account (Posting:Bluesky). Only the Outbox calls it, and only when an admin taps Post.
/// </summary>
public interface IBlueskyPoster
{
    /// <summary>The brand account's handle and app password are set.</summary>
    bool IsConfigured { get; }

    /// <summary>The handle it posts as, for the outbox to show.</summary>
    string? Account { get; }

    /// <summary>
    /// The post (with its link card), then each quote as a reply under it, in order. A quote whose author has turned off
    /// quoting, or that no longer exists, is left out; the post itself either succeeds or throws.
    /// </summary>
    Task<BlueskyPostResult> PostThreadAsync(BlueskyThread thread, CancellationToken ct = default);
}

/// <summary>The link preview under a post: the page it links to, its title and description, and an image.</summary>
public sealed record BlueskyLinkCard(string Url, string Title, string Description, string? ImageUrl);

/// <summary>A reply that quotes another post: a short lead ("The case for:") and the quoted post's at:// address.</summary>
public sealed record BlueskyQuote(string Lead, string AtUri);

/// <summary>
/// One thread: the post's text, the part of it that links to the card's page (it must appear in the text), the card,
/// and the quotes to add as replies.
/// </summary>
public sealed record BlueskyThread(string Text, string LinkText, BlueskyLinkCard Card, IReadOnlyList<BlueskyQuote> Quotes);

/// <summary>The posted thread's first post (at:// and its bsky.app page), and how many quotes made it.</summary>
public sealed record BlueskyPostResult(string Uri, string WebUrl, int QuotesPosted, int QuotesSkipped);
