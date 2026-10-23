using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Home;

/// <summary>What the search box shows before anything is typed.</summary>
public sealed class SearchSuggestionsDto
{
    /// <summary>The titles with the most counted posts this week, most first.</summary>
    public IReadOnlyList<TalkedAboutDto> TalkedAbout { get; init; } = Array.Empty<TalkedAboutDto>();

    /// <summary>TMDB's trending movies and shows, minus any already in <see cref="TalkedAbout"/>.</summary>
    public IReadOnlyList<Title> Trending { get; init; } = Array.Empty<Title>();

    /// <summary>The window the post counts cover, in days.</summary>
    public int WindowDays { get; init; }
}

public sealed class TalkedAboutDto
{
    public Title Title { get; init; } = null!;

    /// <summary>Counted posts in the window (SocialMeter:FeaturedWindowDays).</summary>
    public int Posts { get; init; }
}
