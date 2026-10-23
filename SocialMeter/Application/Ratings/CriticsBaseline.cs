using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Ratings;

/// <summary>
/// What the crowd score is compared against. Movies and shows: the critics, meaning Rotten Tomatoes' Tomatometer and
/// Metacritic's Metascore averaged on the 0-100 scale; TMDB and IMDb are audience ratings and stay out, or "crowd vs
/// critics" would partly compare the crowd with itself. Games: Steam's all-time share of positive reviews.
/// </summary>
public static class CriticsBaseline
{
    public static readonly IReadOnlyList<string> CriticSources = new[] { "rottenTomatoes", "metacritic" };

    /// <summary>The baseline and what to call it ("critics" / "Steam all-time"); Score is null when none is available.</summary>
    public static (int? Score, string Label) For(MediaType mediaType, IReadOnlyList<ExternalRating> ratings)
    {
        if (mediaType == MediaType.Game)
            return (ratings.FirstOrDefault(r => r.Source == "steam")?.Normalized, "Steam all-time");

        var critics = ratings.Where(r => CriticSources.Contains(r.Source) && r.Normalized is not null).Select(r => r.Normalized!.Value).ToList();
        return (critics.Count > 0 ? (int)Math.Round(critics.Average()) : null, "critics");
    }
}
