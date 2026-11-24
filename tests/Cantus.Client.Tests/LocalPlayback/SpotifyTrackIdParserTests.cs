using Cantus.Client.LocalPlayback;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class SpotifyTrackIdParserTests
{
    private const string ID = MprisHostMaps.SPOTIFY_TRACK_ID;

    [Theory]
    [InlineData("/com/spotify/track/" + ID, ID)]
    [InlineData("spotify:track:" + ID, ID)]
    [InlineData("  /com/spotify/track/" + ID + " ", ID)]
    [InlineData("/com/spotify/episode/" + ID, null)]
    [InlineData("/com/spotify/ad/" + ID, null)]
    [InlineData("spotify:local:Artist:Album:Title:200", null)]
    [InlineData("/com/spotify/local/abc", null)]
    [InlineData("/com/spotify/track/tooShort", null)]
    [InlineData("/com/spotify/track/5xfw4Gtv8QZuG3e3jXwek!", null)]
    [InlineData("/com/spotify/track/" + ID + "X", null)]
    [InlineData("/org/chromium/MediaPlayer2/TrackList/NoTrack", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FromTrackId_ReadsOnlyTrackIds(string? trackId, string? expected)
    {
        SpotifyTrackIdParser.FromTrackId(trackId).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://open.spotify.com/track/" + ID, ID)]
    [InlineData("https://open.spotify.com/track/" + ID + "?si=abc123", ID)]
    [InlineData("https://open.spotify.com/intl-de/track/" + ID, ID)]
    [InlineData("https://open.spotify.com/episode/" + ID, null)]
    [InlineData("https://example.com/track/" + ID, null)]
    [InlineData("file:///home/user/Music/song.mp3", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void FromUrl_ReadsOnlyOpenSpotifyTrackLinks(string? url, string? expected)
    {
        SpotifyTrackIdParser.FromUrl(url).Should().Be(expected);
    }

    [Fact]
    public void Parse_PrefersTheTrackIdAndFallsBackToTheUrl()
    {
        SpotifyTrackIdParser.Parse("/com/spotify/track/" + ID, "https://open.spotify.com/track/2mlNgAeIBnL78ZriXgrRHz")
            .Should().Be(ID);
        SpotifyTrackIdParser.Parse("/org/mpris/MediaPlayer2/Track/1", "https://open.spotify.com/track/" + ID)
            .Should().Be(ID);
        SpotifyTrackIdParser.Parse(null, null).Should().BeNull();
    }
}
