using System.Text.Json;
using SocialMeter.Infrastructure.Tmdb.Dtos;
using SocialMeter.Infrastructure.Trailers;
using Xunit;

namespace SocialMeter.Tests;

public class TrailerPickerTests
{
    private static TmdbVideo V(string key, string type = "Trailer", bool official = true, string lang = "en", string date = "2026-09-01", string site = "YouTube", string name = "Official Trailer")
        => new() { Key = key, Type = type, Official = official, Language = lang, PublishedAt = DateTimeOffset.Parse(date), Site = site, Name = name };

    [Fact]
    public void Prefers_an_official_english_trailer_and_the_newest_one()
    {
        var pick = TrailerPicker.FromTmdb(new[]
        {
            V("teaserAAAAA", type: "Teaser", date: "2026-10-01"),
            V("fanCutBBBBB", official: false, date: "2026-10-10"),
            V("spanishCCCC", lang: "es", date: "2026-10-05"),
            V("firstDDDDDD", date: "2026-06-01", name: "Official Trailer"),
            V("finalEEEEEE", date: "2026-09-20", name: "Final Trailer"),
            V("featureFFFF", type: "Featurette", date: "2026-10-12")
        });

        Assert.Equal("youtube", pick!.Provider);
        Assert.Equal("finalEEEEEE", pick.YouTubeId);
        Assert.Equal("Final Trailer", pick.Name);
    }

    [Fact]
    public void A_video_called_a_trailer_beats_a_newer_promo_spot_filed_as_one()
        => Assert.Equal("officialAAA", TrailerPicker.FromTmdb(new[]
        {
            V("officialAAA", date: "2026-07-01", name: "Official Trailer"),
            V("planeBBBBBB", date: "2026-10-15", name: "The Plane")
        })!.YouTubeId);

    [Fact]
    public void Steam_prefers_a_trailer_over_a_demo_reel()
    {
        var data = JsonDocument.Parse("""
        { "movies": [
            { "name": "E3 Demo (Excursion Funnels)", "highlight": true, "hls_h264": "https://video.akamai.steamstatic.com/d/hls_264_master.m3u8" },
            { "name": "Launch Trailer", "highlight": true, "hls_h264": "https://video.akamai.steamstatic.com/t/hls_264_master.m3u8" }
        ] }
        """).RootElement;
        Assert.Equal("Launch Trailer", TrailerPicker.FromSteam(data)!.Name);
    }

    [Fact]
    public void A_teaser_is_used_when_there_is_no_trailer()
        => Assert.Equal("teaserAAAAA", TrailerPicker.FromTmdb(new[] { V("teaserAAAAA", type: "Teaser"), V("clipBBBBBBB", type: "Clip") })!.YouTubeId);

    [Theory]
    [InlineData("short")]                      // not 11 characters
    [InlineData("abc\"><scrip")]               // not a YouTube id at all
    public void Anything_that_is_not_a_youtube_id_is_refused(string key)
        => Assert.Null(TrailerPicker.FromTmdb(new[] { V(key) }));

    [Fact]
    public void Other_video_sites_and_empty_lists_give_no_trailer()
    {
        Assert.Null(TrailerPicker.FromTmdb(new[] { V("vimeoAAAAAA", site: "Vimeo") }));
        Assert.Null(TrailerPicker.FromTmdb(null));
    }

    [Fact]
    public void Steam_prefers_a_highlighted_movie_on_steams_cdn()
    {
        var data = JsonDocument.Parse("""
        { "movies": [
            { "name": "Old gameplay", "highlight": false, "hls_h264": "https://video.akamai.steamstatic.com/a/hls_264_master.m3u8", "thumbnail": "https://shared.akamai.steamstatic.com/a.jpg" },
            { "name": "Launch Trailer", "highlight": true, "hls_h264": "https://video.akamai.steamstatic.com/b/hls_264_master.m3u8", "thumbnail": "https://shared.akamai.steamstatic.com/b.jpg" }
        ] }
        """).RootElement;

        var pick = TrailerPicker.FromSteam(data)!;
        Assert.Equal("steam", pick.Provider);
        Assert.Equal("Launch Trailer", pick.Name);
        Assert.Equal("https://video.akamai.steamstatic.com/b/hls_264_master.m3u8", pick.StreamUrl);
        Assert.Equal("https://shared.akamai.steamstatic.com/b.jpg", pick.Thumbnail);
    }

    [Fact]
    public void Steam_streams_off_steams_cdn_or_without_hls_are_refused()
    {
        var data = JsonDocument.Parse("""
        { "movies": [
            { "name": "Elsewhere", "highlight": true, "hls_h264": "https://evil.example.com/x.m3u8" },
            { "name": "Dash only", "highlight": true, "dash_h264": "https://video.akamai.steamstatic.com/c/dash_h264.mpd" }
        ] }
        """).RootElement;
        Assert.Null(TrailerPicker.FromSteam(data));
        Assert.Null(TrailerPicker.FromSteam(JsonDocument.Parse("{}").RootElement));
    }
}
