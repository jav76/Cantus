using System;
using System.Collections.Generic;
using Cantus.Client.LocalPlayback;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class MprisMetadataParserTests
{
    [Fact]
    public void Parse_SpotifyMetadataFromTheHost_ReadsEveryField()
    {
        // Act
        LocalTrack? track = MprisMetadataParser.Parse(MprisHostMaps.SpotifyMetadata());

        // Assert
        track.Should().Be(new LocalTrack(
            "Ballin",
            "parkbreezy",
            "Reflections",
            TimeSpan.FromSeconds(184),
            "https://i.scdn.co/image/ab67616d0000b273cdfe2656c37742ac1beeb7b8",
            MprisHostMaps.SPOTIFY_TRACK_ID));
    }

    [Fact]
    public void Parse_IdleBrowserMetadata_IsNoTrack()
    {
        MprisMetadataParser.Parse(MprisHostMaps.IdleBrowserMetadata()).Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyOrMissingMetadata_IsNoTrack()
    {
        MprisMetadataParser.Parse(new Dictionary<string, object?>()).Should().BeNull();
        MprisMetadataParser.Parse(null).Should().BeNull();
    }

    [Fact]
    public void Parse_JoinsSeveralArtistsAndSkipsBlankOnes()
    {
        // Arrange
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata["xesam:artist"] = new[] { "Joji", " ", "BENEE " };

        // Act & Assert
        MprisMetadataParser.Parse(metadata)!.Artist.Should().Be("Joji, BENEE");
    }

    [Fact]
    public void Parse_AcceptsASingleArtistStringAndAnObjectArray()
    {
        // Arrange
        Dictionary<string, object?> single = MprisHostMaps.SpotifyMetadata();
        single["xesam:artist"] = "Joji";
        Dictionary<string, object?> objects = MprisHostMaps.SpotifyMetadata();
        objects["xesam:artist"] = new object?[] { "Joji", null, "BENEE" };

        // Act & Assert
        MprisMetadataParser.Parse(single)!.Artist.Should().Be("Joji");
        MprisMetadataParser.Parse(objects)!.Artist.Should().Be("Joji, BENEE");
    }

    [Theory]
    [InlineData(184000000UL)]
    [InlineData(184000000L)]
    [InlineData(184000000.0)]
    public void Parse_ReadsTheLengthWhateverItsIntegerType(object length)
    {
        // Arrange
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata["mpris:length"] = length;

        // Act & Assert
        MprisMetadataParser.Parse(metadata)!.Duration.Should().Be(TimeSpan.FromSeconds(184));
    }

    [Fact]
    public void Parse_NegativeOrMissingLength_IsZero()
    {
        // Arrange
        Dictionary<string, object?> negative = MprisHostMaps.SpotifyMetadata();
        negative["mpris:length"] = -5L;
        Dictionary<string, object?> missing = MprisHostMaps.SpotifyMetadata();
        missing.Remove("mpris:length");

        // Act & Assert
        MprisMetadataParser.Parse(negative)!.Duration.Should().Be(TimeSpan.Zero);
        MprisMetadataParser.Parse(missing)!.Duration.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("file:///tmp/.org.chromium.Chromium.abc/cover.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("not a url")]
    public void Parse_KeepsOnlyHttpArtUrls(string artUrl)
    {
        // Arrange
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata["mpris:artUrl"] = artUrl;

        // Act & Assert
        MprisMetadataParser.Parse(metadata)!.AlbumArtUrl.Should().BeNull();
    }

    [Fact]
    public void Parse_WithoutASpotifyIdentity_LeavesTheSpotifyIdUnset()
    {
        // Arrange: a browser tab with its own object-path track ID and no Spotify URL.
        Dictionary<string, object?> metadata = new()
        {
            ["mpris:trackid"] = "/org/chromium/MediaPlayer2/TrackList/Track1",
            ["mpris:length"] = 215000000L,
            ["xesam:title"] = "Glimpse of Us",
            ["xesam:artist"] = new[] { "Joji" },
            ["xesam:album"] = "",
            ["xesam:url"] = "https://www.youtube.com/watch?v=abc",
        };

        // Act
        LocalTrack? track = MprisMetadataParser.Parse(metadata);

        // Assert
        track!.SpotifyTrackId.Should().BeNull();
        track.Album.Should().BeNull();
        track.Title.Should().Be("Glimpse of Us");
    }

    [Fact]
    public void ReadTrackObjectPath_ReturnsTheRawTrackId()
    {
        MprisMetadataParser.ReadTrackObjectPath(MprisHostMaps.SpotifyMetadata())
            .Should().Be("/com/spotify/track/" + MprisHostMaps.SPOTIFY_TRACK_ID);
        MprisMetadataParser.ReadTrackObjectPath(MprisHostMaps.IdleBrowserMetadata()).Should().BeNull();
    }
}
