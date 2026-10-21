using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Home;

public sealed class HomeResponseDto
{
    public Title? Featured { get; init; }
    public IReadOnlyList<HomeRowDto> Rows { get; init; } = Array.Empty<HomeRowDto>();
}

public sealed class HomeRowDto
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public IReadOnlyList<Title> Items { get; init; } = Array.Empty<Title>();
}
