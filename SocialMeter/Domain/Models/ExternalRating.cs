namespace SocialMeter.Domain.Models;

/// <summary>A rating from another site, normalized to 0–100 so it can sit beside the SocialMeter score.</summary>
public sealed class ExternalRating
{
    /// <summary>"tmdb", "imdb", "rottenTomatoes", "metacritic".</summary>
    public string Source { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>The rating as the source shows it, e.g. "8.3/10", "92%". Null when unavailable.</summary>
    public string? Value { get; init; }

    /// <summary>0–100, null when unavailable.</summary>
    public int? Normalized { get; init; }

    /// <summary>Extra context such as vote count.</summary>
    public string? Detail { get; init; }

    public string? Url { get; init; }

    public bool Available => Normalized is not null;
}
