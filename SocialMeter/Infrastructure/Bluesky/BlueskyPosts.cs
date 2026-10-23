using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SocialMeter.Application.Social;
using SocialMeter.Domain.Models;

namespace SocialMeter.Infrastructure.Bluesky;

/// <summary>
/// Turns Bluesky post views (from searchPosts or getPosts) into post candidates, applying the same rules the X search
/// applies on X's side: no replies, the post itself must say what kind of title it is talking about (or, for an
/// everyday-word name, name one of its context terms), and promo phrases are dropped. Pure, so the rules are tested without the network.
/// </summary>
public static partial class BlueskyPosts
{
    public const string Platform = "bluesky";
    private const string PostCollection = "app.bsky.feed.post";

    /// <summary>Labels Bluesky's moderation puts on adult or graphic posts; their media is hidden by default.</summary>
    private static readonly HashSet<string> SensitiveLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "porn", "sexual", "nudity", "graphic-media", "gore"
    };

    /// <summary>
    /// The search text. Bluesky's search takes Elasticsearch-style syntax: quotes for a phrase, a space for AND, "|" for
    /// OR (the word OR is not understood). An everyday-word name must come with one of its context terms:
    /// "FROM" (Fromville | "Harold Perrineau"). Measured 2026-10-23: plain "FROM" gave 276 posts and none about the show;
    /// with context, 10 posts and 8 about it.
    /// </summary>
    public static string Query(TitleSearch search) => search.Ambiguous && search.Context.Count > 0
        ? $"\"{search.Name}\" ({string.Join(" | ", search.Context.Select(t => t.Contains(' ') ? $"\"{t}\"" : t))})"
        : $"\"{search.Name}\"";

    /// <param name="context">For an everyday-word name, the terms that pin it down: a post that names one of them counts
    /// even without a media word ("Seasons 1-3 of From, starring Harold Perrineau"). Null for distinctive names.</param>
    public static SocialPostCandidate? ToCandidate(JsonElement post, MediaType mediaType, IReadOnlyCollection<string> excludeTerms,
        IReadOnlyList<string>? context = null)
    {
        if (ExternalIdFromUri(Str(post, "uri")) is not { } externalId) return null;
        if (!post.TryGetProperty("record", out var record) || record.ValueKind != JsonValueKind.Object) return null;

        // Replies are mostly conversation, not reactions; X's search drops them too.
        if (record.TryGetProperty("reply", out _)) return null;

        var text = Str(record, "text");
        if (string.IsNullOrWhiteSpace(text)) return null;
        var namesContext = context?.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)) == true;
        if (!namesContext && !ContextWords(mediaType).IsMatch(text)) return null;
        if (excludeTerms.Any(t => t.Trim().Length > 0 && text.Contains(t.Trim(), StringComparison.OrdinalIgnoreCase))) return null;

        var author = post.TryGetProperty("author", out var a) ? a : default;
        var did = author.ValueKind == JsonValueKind.Object ? Str(author, "did") : null;
        var handle = author.ValueKind == JsonValueKind.Object ? Str(author, "handle") : null;
        handle = string.IsNullOrWhiteSpace(handle) || handle == "handle.invalid" ? did ?? "unknown" : handle;

        // The author sets createdAt; Bluesky sets indexedAt. A post dated in the future gets the time Bluesky saw it.
        var created = Date(record, "createdAt");
        var indexed = Date(post, "indexedAt");
        var when = created is { } c && (indexed is null || c <= indexed) ? c : indexed ?? DateTime.UtcNow;

        var media = new List<PostMedia>();
        if (post.TryGetProperty("embed", out var embed)) CollectMedia(embed, media);

        var rkey = externalId[(externalId.LastIndexOf('/') + 1)..];
        return new SocialPostCandidate(
            Platform,
            externalId,
            handle,
            text.Trim(),
            Int(post, "likeCount"),
            Int(post, "replyCount"),
            when,
            $"https://bsky.app/profile/{handle}/post/{rkey}",
            null,
            AuthorId: did,
            Media: media.Count > 0 ? media : null,
            Sensitive: HasSensitiveLabel(post));
    }

    /// <summary>"at://did:plc:abc/app.bsky.feed.post/3kxyz" → "did:plc:abc/3kxyz", which fits the 64-character id column.</summary>
    public static string? ExternalIdFromUri(string? uri)
    {
        if (uri is null || !uri.StartsWith("at://", StringComparison.Ordinal)) return null;
        var parts = uri["at://".Length..].Split('/');
        if (parts.Length != 3 || parts[1] != PostCollection || parts[0].Length == 0 || parts[2].Length == 0) return null;
        var id = $"{parts[0]}/{parts[2]}";
        return id.Length <= 64 ? id : null;
    }

    /// <summary>The reverse of <see cref="ExternalIdFromUri"/>, for re-checking stored posts.</summary>
    public static string? UriFromExternalId(string externalId)
    {
        var slash = externalId.LastIndexOf('/');
        if (slash <= 0 || slash == externalId.Length - 1) return null;
        return $"at://{externalId[..slash]}/{PostCollection}/{externalId[(slash + 1)..]}";
    }

    private static void CollectMedia(JsonElement embed, List<PostMedia> media)
    {
        if (embed.ValueKind != JsonValueKind.Object) return;
        var type = Str(embed, "$type") ?? "";

        if (type.StartsWith("app.bsky.embed.images", StringComparison.Ordinal)
            && embed.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var img in images.EnumerateArray())
            {
                var url = Str(img, "fullsize") ?? Str(img, "thumb");
                if (!string.IsNullOrWhiteSpace(url))
                    media.Add(new PostMedia(url, "photo", NullIfBlank(Str(img, "alt"))));
            }
        }
        else if (type.StartsWith("app.bsky.embed.video", StringComparison.Ordinal))
        {
            var thumb = Str(embed, "thumbnail");
            if (!string.IsNullOrWhiteSpace(thumb))
                media.Add(new PostMedia(thumb, "video", NullIfBlank(Str(embed, "alt"))));
        }
        else if (type.StartsWith("app.bsky.embed.recordWithMedia", StringComparison.Ordinal)
                 && embed.TryGetProperty("media", out var inner))
        {
            CollectMedia(inner, media);
        }
        // Link cards (external) and quoted posts are someone else's content, not the author's opinion: skipped, as on X.
    }

    private static bool HasSensitiveLabel(JsonElement post)
        => post.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
           && labels.EnumerateArray().Any(l => Str(l, "val") is { } v && SensitiveLabels.Contains(v));

    /// <summary>The words X's query requires, matched here because Bluesky's search has no OR.</summary>
    private static Regex ContextWords(MediaType mediaType) => mediaType switch
    {
        MediaType.Tv => TvWords(),
        MediaType.Game => GameWords(),
        _ => MovieWords()
    };

    [GeneratedRegex(@"\b(movies?|films?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MovieWords();

    [GeneratedRegex(@"\b(shows?|series|seasons?|episodes?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TvWords();

    [GeneratedRegex(@"\b(games?|steam|playing|played|gameplay)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GameWords();

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static DateTime? Date(JsonElement e, string name)
        => Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : null;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
