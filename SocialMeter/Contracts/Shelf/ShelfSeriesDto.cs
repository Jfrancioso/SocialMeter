namespace SocialMeter.Contracts.Shelf;

/// <summary>One series on the private reading shelf: its main books in reading order, and its first book's cover.</summary>
public sealed record ShelfSeriesDto(string Name, string Author, string Pitch, int CoverId, IReadOnlyList<string> Books);
