namespace SocialMeter.Domain.Models;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int TotalPages,
    int TotalResults
);
