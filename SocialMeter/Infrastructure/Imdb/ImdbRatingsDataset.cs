using System.Globalization;
using System.IO.Compression;

namespace SocialMeter.Infrastructure.Imdb;

/// <summary>
/// IMDb's daily title.ratings.tsv.gz, held in memory as three parallel arrays sorted by numeric title id (about 20 MB
/// for 1.7 million titles). OMDb lags IMDb by days or weeks for new releases; this file has a rating as soon as IMDb does.
/// Swapped atomically on refresh, so readers never see a half-loaded table.
/// </summary>
public sealed class ImdbRatingsDataset
{
    private sealed record Table(int[] Ids, float[] Ratings, int[] Votes, DateTime LoadedAt);

    private Table? _table;

    public bool IsLoaded => _table is not null;
    public DateTime? LoadedAt => _table?.LoadedAt;
    public int Count => _table?.Ids.Length ?? 0;

    /// <summary>The rating and vote count for an IMDb id such as "tt0111161"; false when unknown or not loaded.</summary>
    public bool TryGet(string? imdbId, out double rating, out int votes)
    {
        rating = 0;
        votes = 0;
        var table = _table;
        if (table is null || !TryParseId(imdbId, out var id)) return false;

        var i = Array.BinarySearch(table.Ids, id);
        if (i < 0) return false;
        rating = table.Ratings[i];
        votes = table.Votes[i];
        return true;
    }

    /// <summary>Parses a gzipped ratings file and replaces the current table.</summary>
    public async Task LoadAsync(Stream gzipped, CancellationToken ct)
    {
        var ids = new List<int>(1_800_000);
        var ratings = new List<float>(1_800_000);
        var votes = new List<int>(1_800_000);

        await using var gz = new GZipStream(gzipped, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        await reader.ReadLineAsync(ct); // header: tconst, averageRating, numVotes

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var a = line.IndexOf('\t');
            var b = a < 0 ? -1 : line.IndexOf('\t', a + 1);
            if (b < 0) continue;
            if (!TryParseId(line.AsSpan(0, a), out var id)) continue;
            if (!float.TryParse(line.AsSpan(a + 1, b - a - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) continue;
            if (!int.TryParse(line.AsSpan(b + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) continue;
            ids.Add(id);
            ratings.Add(r);
            votes.Add(v);
        }

        // The file is sorted as text, which is not numeric order once ids reach eight digits.
        var idArray = ids.ToArray();
        var order = Enumerable.Range(0, idArray.Length).ToArray();
        Array.Sort(idArray, order);
        var ratingArray = new float[order.Length];
        var voteArray = new int[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            ratingArray[i] = ratings[order[i]];
            voteArray[i] = votes[order[i]];
        }

        _table = new Table(idArray, ratingArray, voteArray, DateTime.UtcNow);
    }

    private static bool TryParseId(string? s, out int id) { id = 0; return s is not null && TryParseId(s.AsSpan(), out id); }

    private static bool TryParseId(ReadOnlySpan<char> s, out int id)
    {
        id = 0;
        return s.Length > 2 && s[0] == 't' && s[1] == 't'
               && int.TryParse(s[2..], NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
}
