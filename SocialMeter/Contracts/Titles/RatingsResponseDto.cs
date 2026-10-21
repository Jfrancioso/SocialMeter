using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Titles;

public sealed class RatingsResponseDto
{
    public int TmdbId { get; init; }
    public MediaType MediaType { get; init; }
    public IReadOnlyList<ExternalRating> Ratings { get; init; } = Array.Empty<ExternalRating>();

    /// <summary>Average of the available normalized ratings, or null when none are available.</summary>
    public int? CriticsAverage { get; init; }
}
