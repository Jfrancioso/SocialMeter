namespace SocialMeter.Infrastructure.Tmdb.Dtos;

public sealed class TmdbPagedResponse<T>
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalResults { get; set; }

    public List<T> Results { get; set; } = new();
}
