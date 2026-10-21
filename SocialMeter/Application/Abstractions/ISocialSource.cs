using SocialMeter.Domain.Models;

namespace SocialMeter.Application.Abstractions;

/// <summary>A social platform SocialMeter can pull posts from. Register one per platform.</summary>
public interface ISocialSource
{
    /// <summary>Stable key stored on each post: "reddit", "x".</summary>
    string Platform { get; }

    /// <summary>False when the credentials the source needs are missing; the source is then reported but skipped.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<SocialPostCandidate>> FetchAsync(Title title, CancellationToken ct = default);
}
