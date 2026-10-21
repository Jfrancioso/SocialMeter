using System.Text.Json.Serialization;

namespace SocialMeter.Infrastructure.Tmdb.Dtos;

public sealed class TmdbPagedResponse<T>
{
    public int Page { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("total_results")] public int TotalResults { get; set; }
    public List<T> Results { get; set; } = new();
}
