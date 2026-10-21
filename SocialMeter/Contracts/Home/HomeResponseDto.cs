using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Home;

public sealed class HomeResponseDto
{
    public Movie? Featured { get; init; }
    public List<HomeRowDto> Rows { get; init; } = new();
}
