using SocialMeter.Domain.Models;

namespace SocialMeter.Contracts.Titles;

public sealed class RatingsResponseDto
{
    public int TmdbId { get; init; }
    public MediaType MediaType { get; init; }
    public IReadOnlyList<ExternalRating> Ratings { get; init; } = Array.Empty<ExternalRating>();

    /// <summary>
    /// The number the SocialMeter reading is compared against: the critics' average for movies and shows,
    /// Steam's all-time score for games. Null when nothing is available.
    /// </summary>
    public int? CriticsAverage { get; init; }

    /// <summary>What <see cref="CriticsAverage"/> is, for the comparison line: "critics" or "Steam all-time".</summary>
    public string BaselineLabel { get; init; } = "critics";
}
