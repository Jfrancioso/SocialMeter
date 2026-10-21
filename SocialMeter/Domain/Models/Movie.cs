namespace SocialMeter.Domain.Models
{
    public class Movie
    {
        public int TmdbId { get; set; }
        public string Title { get; set; } = "";
        public DateOnly? ReleaseDate { get; set; }
        public string PosterPath { get; set; } = "";
        public MovieCategory Category { get; set; }
        public MediaType MediaType { get; set; }
        public string BackdropPath { get; set; } = "";
        public string Overview { get; set; } = "";
        public double VoteAverage { get; set; }

    }
}
