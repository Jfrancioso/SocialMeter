using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Home;

public sealed class HomeRowDto
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<Movie> Items { get; init; } = Array.Empty<Movie>();

    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalResults { get; init; }
}
