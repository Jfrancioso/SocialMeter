using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Tracking;

/// <summary>The genre theme SocialMeter leans into for a given month, with TMDB genre ids for movies and TV.</summary>
public sealed record SeasonalTheme(string Key, string Label, string Subtitle, int MovieGenreId, int TvGenreId)
{
    public static SeasonalTheme For(DateTime utcNow) => utcNow.Month switch
    {
        10 => new("halloween", "Halloween picks", "Horror is in season", 27, 9648),            // Horror / Mystery
        11 => new("thanksgiving", "Family table", "Something everyone can watch", 10751, 10751), // Family
        12 => new("holidays", "Holiday watch", "Cozy season", 10751, 35),                        // Family / Comedy
        1 => new("awards", "Awards season", "The prestige slate", 18, 18),                       // Drama
        2 => new("romance", "Love stories", "Valentine's month", 10749, 18),                     // Romance / Drama
        6 or 7 or 8 => new("summer", "Summer blockbusters", "Big, loud, popcorn", 28, 10759),    // Action / Action & Adventure
        _ => new("thrills", "Edge of your seat", "Thrillers and mysteries", 53, 9648)            // Thriller / Mystery
    };

    public int GenreFor(MediaType mediaType) => mediaType == MediaType.Tv ? TvGenreId : MovieGenreId;
}
