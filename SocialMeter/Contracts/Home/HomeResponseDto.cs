using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Home;

public sealed class HomeResponseDto
{
    public Title? Featured { get; init; }

    /// <summary>"Most talked about" when the hero was chosen from Chatterbub's own posts; "Trending on TMDB" when none qualified.</summary>
    public string FeaturedLabel { get; init; } = "";

    /// <summary>Counted posts behind the pick over the last <see cref="FeaturedWindowDays"/> days; null for the TMDB fallback.</summary>
    public int? FeaturedPosts { get; init; }

    public int FeaturedWindowDays { get; init; }
    public IReadOnlyList<HomeRowDto> Rows { get; init; } = Array.Empty<HomeRowDto>();
}

public sealed class HomeRowDto
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public IReadOnlyList<Title> Items { get; init; } = Array.Empty<Title>();
}
