using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Tracking;

/// <summary>
/// The genre theme Chatterbub leans into for a given month, with TMDB genre ids for movies and TV. TMDB has no horror
/// genre for TV, so October's shows come from the "horror" keyword instead (TvKeywordId); its Mystery genre was a poor
/// stand-in that filled the row with crime procedurals.
/// </summary>
public sealed record SeasonalTheme(string Key, string Label, string Subtitle, int MovieGenreId, int TvGenreId, int? TvKeywordId = null)
{
    /// <summary>TMDB's "horror" keyword.</summary>
    public const int HorrorKeyword = 315058;

    public static SeasonalTheme For(DateTime utcNow) => utcNow.Month switch
    {
        10 => new("halloween", "Halloween picks", "Horror is in season", 27, 9648, HorrorKeyword), // Horror / "horror" keyword
        11 => new("thanksgiving", "Family table", "Something everyone can watch", 10751, 10751), // Family
        12 => new("holidays", "Holiday watch", "Cozy season", 10751, 35),                        // Family / Comedy
        1 => new("awards", "Awards season", "The prestige slate", 18, 18),                       // Drama
        2 => new("romance", "Love stories", "Valentine's month", 10749, 18),                     // Romance / Drama
        6 or 7 or 8 => new("summer", "Summer blockbusters", "Big, loud, popcorn", 28, 10759),    // Action / Action & Adventure
        _ => new("thrills", "Edge of your seat", "Thrillers and mysteries", 53, 9648)            // Thriller / Mystery
    };
}
