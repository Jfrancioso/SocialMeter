using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

public interface IRatingsService
{
    /// <summary>Ratings from TMDB plus, when OMDb is configured, IMDb / Rotten Tomatoes / Metacritic.</summary>
    Task<IReadOnlyList<ExternalRating>> GetRatingsAsync(Title title, CancellationToken ct = default);
}
