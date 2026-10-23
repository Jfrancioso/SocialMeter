using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Tracking;

/// <summary>What Chatterbub knows about a candidate: counted posts this week, and counted posts overall (null if never measured).</summary>
public sealed record ConversationSignal(int RecentPosts, int? TotalPosts);

/// <summary>
/// Orders a provider's candidate list by the conversation Chatterbub actually measured, so a row that says people are
/// talking about these titles is true. TMDB supplies the candidates (its popularity reflects page views on TMDB, which a
/// lookalike release can inflate); posts decide the order.
/// </summary>
public static class ConversationRanking
{
    /// <summary>
    /// Most counted posts this week first, then most overall; then titles not measured yet, in the provider's order, so a
    /// new release still gets seen and measured. Titles measured and found to have no counted posts are dropped.
    /// </summary>
    public static IReadOnlyList<Title> Rank(IReadOnlyList<Title> candidates, IReadOnlyDictionary<int, ConversationSignal> signals, int take)
    {
        var indexed = candidates
            .Select((title, order) => (title, order, signal: signals.GetValueOrDefault(title.TmdbId)))
            .GroupBy(x => x.title.TmdbId)
            .Select(g => g.First())
            .ToList();

        var talkedAbout = indexed
            .Where(x => x.signal?.TotalPosts > 0)
            .OrderByDescending(x => x.signal!.RecentPosts)
            .ThenByDescending(x => x.signal!.TotalPosts)
            .ThenBy(x => x.order);

        var notMeasured = indexed
            .Where(x => x.signal?.TotalPosts is null)
            .OrderBy(x => x.order);

        return talkedAbout.Concat(notMeasured).Select(x => x.title).Take(Math.Max(0, take)).ToList();
    }
}
