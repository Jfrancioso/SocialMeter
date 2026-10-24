using System.Text;
using System.Text.Json.Nodes;

namespace SocialMeter.Infrastructure.Bluesky;

/// <summary>
/// The JSON records the brand account writes (app.bsky.feed.post), as pure functions so the tests pin their shape. A link
/// in a post is a "facet": a byte range of the UTF-8 text (not characters) marked as a link.
/// </summary>
public static class BlueskyRecords
{
    public const string PostCollection = "app.bsky.feed.post";

    /// <summary>The UTF-8 byte range of <paramref name="part"/> in <paramref name="text"/>; null when it isn't there.</summary>
    public static (int Start, int End)? ByteRange(string text, string part)
    {
        var at = text.IndexOf(part, StringComparison.Ordinal);
        if (at < 0 || part.Length == 0) return null;
        var start = Encoding.UTF8.GetByteCount(text.AsSpan(0, at));
        return (start, start + Encoding.UTF8.GetByteCount(part));
    }

    /// <summary>The thread's first post: its text with the link marked, and the link card (with the uploaded image, if any).</summary>
    public static JsonObject Post(string text, string linkText, string url, string cardTitle, string cardDescription, JsonNode? thumb, DateTime now)
    {
        var record = Base(text, now);
        if (ByteRange(text, linkText) is { } range)
        {
            record["facets"] = new JsonArray(new JsonObject
            {
                ["index"] = new JsonObject { ["byteStart"] = range.Start, ["byteEnd"] = range.End },
                ["features"] = new JsonArray(new JsonObject { ["$type"] = "app.bsky.richtext.facet#link", ["uri"] = url })
            });
        }

        var external = new JsonObject { ["uri"] = url, ["title"] = cardTitle, ["description"] = cardDescription };
        if (thumb is not null) external["thumb"] = thumb.DeepClone();
        record["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.external", ["external"] = external };
        return record;
    }

    /// <summary>A reply in the thread that quotes another post.</summary>
    public static JsonObject QuoteReply(string lead, (string Uri, string Cid) root, (string Uri, string Cid) parent, (string Uri, string Cid) quoted, DateTime now)
    {
        var record = Base(lead, now);
        record["reply"] = new JsonObject { ["root"] = Ref(root), ["parent"] = Ref(parent) };
        record["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.record", ["record"] = Ref(quoted) };
        return record;
    }

    /// <summary>"https://bsky.app/profile/{did}/post/{rkey}" for an at://{did}/app.bsky.feed.post/{rkey} address.</summary>
    public static string WebUrl(string atUri)
    {
        var parts = atUri.Replace("at://", "").Split('/');
        return parts.Length == 3 ? $"https://bsky.app/profile/{parts[0]}/post/{parts[2]}" : atUri;
    }

    private static JsonObject Base(string text, DateTime now) => new()
    {
        ["$type"] = PostCollection,
        ["text"] = text,
        ["createdAt"] = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
        ["langs"] = new JsonArray("en")
    };

    private static JsonObject Ref((string Uri, string Cid) r) => new() { ["uri"] = r.Uri, ["cid"] = r.Cid };
}
