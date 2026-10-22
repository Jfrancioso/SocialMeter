using System.IO.Compression;
using System.Text;
using SocialMeter.Infrastructure.Imdb;
using SocialMeter.Infrastructure.Links;
using Xunit;

namespace SocialMeter.Tests;

public class PublishedScoreReaderTests
{
    private static string Page(string jsonLd) => $"<html><head><script type=\"application/ld+json\">{jsonLd}</script></head></html>";

    [Fact]
    public void Reads_the_tomatometer_from_rotten_tomatoes_structured_data()
    {
        var html = Page("{\"@type\":\"Movie\",\"name\":\"Clayface\",\"aggregateRating\":{\"@type\":\"AggregateRating\",\"name\":\"Tomatometer\",\"ratingValue\":\"75\",\"bestRating\":\"100\",\"reviewCount\":107}}");
        Assert.Equal(new PublishedScore(75, 107), PublishedScoreReader.Parse(html, "Tomatometer"));
    }

    [Fact]
    public void Reads_a_numeric_metascore()
    {
        var html = Page("{\"@type\":\"Movie\",\"aggregateRating\":{\"name\":\"Metascore\",\"ratingValue\":59,\"bestRating\":100,\"reviewCount\":36}}");
        Assert.Equal(new PublishedScore(59, 36), PublishedScoreReader.Parse(html, "Metascore"));
    }

    [Fact]
    public void An_audience_score_is_never_taken_for_the_tomatometer()
    {
        var html = Page("{\"@type\":\"TVSeries\",\"aggregateRating\":{\"name\":\"AudienceScore\",\"ratingValue\":\"96\",\"bestRating\":\"100\"}}");
        Assert.Null(PublishedScoreReader.Parse(html, "Tomatometer"));
    }

    [Fact]
    public void An_older_film_with_the_same_name_does_not_lend_its_score()
    {
        var html = Page("{\"@type\":\"Movie\",\"name\":\"Fresh Meat\",\"dateCreated\":\"2012-08-01\",\"aggregateRating\":{\"name\":\"Tomatometer\",\"ratingValue\":\"60\",\"bestRating\":\"100\"}}");
        Assert.Null(PublishedScoreReader.Parse(html, "Tomatometer", 2026));
        Assert.Equal(60, PublishedScoreReader.Parse(html, "Tomatometer", 2012)!.Value);
        Assert.Equal(60, PublishedScoreReader.Parse(html, "Tomatometer", 2013)!.Value); // festival vs wide release
    }

    [Fact]
    public void A_score_on_another_scale_is_ignored()
    {
        var html = Page("{\"aggregateRating\":{\"name\":\"Metascore\",\"ratingValue\":7.5,\"bestRating\":10}}");
        Assert.Null(PublishedScoreReader.Parse(html, "Metascore"));
    }

    [Fact]
    public void Nested_and_listed_blocks_are_searched_and_broken_ones_skipped()
    {
        var html = "<script type=\"application/ld+json\">{not json</script>" +
                   Page("[{\"@type\":\"BreadcrumbList\"},{\"@graph\":[{\"@type\":\"Movie\",\"aggregateRating\":{\"name\":\"Tomatometer\",\"ratingValue\":\"19\",\"bestRating\":\"100\",\"ratingCount\":93}}]}]");
        Assert.Equal(new PublishedScore(19, 93), PublishedScoreReader.Parse(html, "Tomatometer"));
    }
}

public class ImdbRatingsDatasetTests
{
    private static MemoryStream Gz(string text)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Finds_ratings_including_eight_digit_ids_in_text_sorted_files()
    {
        // Text order puts tt10000000 before tt1000001; the lookup must still find both.
        var file = "tconst\taverageRating\tnumVotes\ntt0111161\t9.3\t3000000\ntt10000000\t6.1\t120\ntt1000001\t7.2\t55\ntt36586020\t5.4\t11724\n";
        var ds = new ImdbRatingsDataset();
        await ds.LoadAsync(Gz(file), CancellationToken.None);

        Assert.Equal(4, ds.Count);
        Assert.True(ds.TryGet("tt36586020", out var r, out var v));
        Assert.Equal(5.4, r, 3);
        Assert.Equal(11724, v);
        Assert.True(ds.TryGet("tt1000001", out r, out _));
        Assert.Equal(7.2, r, 3);
        Assert.True(ds.TryGet("tt10000000", out r, out _));
        Assert.Equal(6.1, r, 3);
        Assert.False(ds.TryGet("tt9999999", out _, out _));
        Assert.False(ds.TryGet(null, out _, out _));
    }

    [Fact]
    public void Nothing_is_found_before_the_first_load()
        => Assert.False(new ImdbRatingsDataset().TryGet("tt0111161", out _, out _));
}
