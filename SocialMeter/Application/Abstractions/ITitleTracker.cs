using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface ITitleTracker
{
    /// <summary>Why the title is tracked, or null when it is locked.</summary>
    Task<string?> GetReasonAsync(MediaType mediaType, int tmdbId, CancellationToken ct = default);

    /// <summary>Stamps Tracked / TrackedReason onto each title.</summary>
    Task AnnotateAsync(IEnumerable<Title> titles, CancellationToken ct = default);

    /// <summary>Rebuilds the tracked set from the current TMDB lists and the seasonal theme.</summary>
    Task<TrackedRefreshResult> RefreshAsync(CancellationToken ct = default);

    /// <summary>The current seasonal theme and the titles it pulls in (for the home page row).</summary>
    Task<(string Label, string Subtitle, IReadOnlyList<Title> Items)> GetSeasonalRowAsync(CancellationToken ct = default);

    /// <summary>
    /// Extra rows some seasons put at the very top of the home page (October: horror trending now, new horror in theaters).
    /// Empty in months that only have the mid-page seasonal row.
    /// </summary>
    Task<IReadOnlyList<SeasonalRow>> GetSeasonalTopRowsAsync(string region, CancellationToken ct = default);
}

/// <summary>A home-page row a season adds: a stable key for the UI, a heading, and the titles.</summary>
public sealed record SeasonalRow(string Key, string Title, string Subtitle, IReadOnlyList<Title> Items);

public sealed class TrackedRefreshResult
{
    public DateTime RefreshedAt { get; init; }
    public int Tracked { get; init; }
    public int Added { get; init; }
    public int Dropped { get; init; }
    public string Theme { get; init; } = "";
}
