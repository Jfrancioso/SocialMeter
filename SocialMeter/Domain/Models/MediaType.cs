namespace SocialMeter.Domain.Models;

/// <summary>Kind of title. Serialized as "movie" / "tv" / "game" and accepted the same way in routes.</summary>
public enum MediaType
{
    Movie,
    Tv,
    Game
}

public static class MediaTypeExtensions
{
    /// <summary>URL segment / TMDB path segment: "movie", "tv", "game".</summary>
    public static string Segment(this MediaType m) => m switch
    {
        MediaType.Tv => "tv",
        MediaType.Game => "game",
        _ => "movie"
    };

    public static string Label(this MediaType m) => m switch
    {
        MediaType.Tv => "TV show",
        MediaType.Game => "video game",
        _ => "movie"
    };

    /// <summary>Words that steer a social search toward the right kind of title.</summary>
    public static string SearchWords(this MediaType m) => m switch
    {
        MediaType.Tv => "(show OR series OR season OR episode)",
        MediaType.Game => "(game OR steam OR playing OR played OR gameplay)",
        _ => "(movie OR film)"
    };

    /// <summary>Movies and shows come from TMDB; games from Steam.</summary>
    public static bool IsTmdb(this MediaType m) => m != MediaType.Game;
}
