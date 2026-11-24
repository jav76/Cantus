using System;
using System.Collections.Generic;
using Cantus.Client.LocalPlayback;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class MprisPlayerStateTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(1000);

    [Fact]
    public void FromTheHostsSpotifyProperties_BuildsTheSnapshot()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.8641");

        // Act
        state.ApplyRootProperties(MprisHostMaps.SpotifyRoot());
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);
        LocalPlayerSnapshot snapshot = state.ToSnapshot();

        // Assert
        snapshot.InstanceId.Should().Be(MprisHostMaps.SPOTIFY_BUS_NAME);
        snapshot.AppId.Should().Be("spotify");
        snapshot.AppName.Should().Be("Spotify");
        snapshot.Status.Should().Be(LocalPlayerStatus.Playing);
        snapshot.Track!.Title.Should().Be("Ballin");
        snapshot.Track.SpotifyTrackId.Should().Be(MprisHostMaps.SPOTIFY_TRACK_ID);
        snapshot.Position.Should().Be(TimeSpan.FromMilliseconds(112283));
        snapshot.PositionCapturedAt.Should().Be(T0);
        snapshot.LastStartedPlayingAt.Should().Be(T0);
        snapshot.Capabilities.Should().Be(new LocalPlayerCapabilities(true, true, true, true, true));
        state.TrackObjectPath.Should().Be("/com/spotify/track/" + MprisHostMaps.SPOTIFY_TRACK_ID);
    }

    [Fact]
    public void FromTheHostsIdleBraveProperties_IsStoppedWithNoTrack()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.BRAVE_BUS_NAME, ":1.5292");

        // Act
        state.ApplyRootProperties(MprisHostMaps.IdleBrowserRoot("Brave"));
        state.ApplyPlayerProperties(MprisHostMaps.IdleBrowserPlayer(), T0);
        LocalPlayerSnapshot snapshot = state.ToSnapshot();

        // Assert
        snapshot.AppId.Should().Be("brave");
        snapshot.AppName.Should().Be("Brave");
        snapshot.Status.Should().Be(LocalPlayerStatus.Stopped);
        snapshot.Track.Should().BeNull();
        snapshot.LastStartedPlayingAt.Should().BeNull();
        snapshot.Capabilities.Should().Be(LocalPlayerCapabilities.None);
    }

    [Fact]
    public void CanControlFalse_TurnsEveryCapabilityOff()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        Dictionary<string, object?> player = MprisHostMaps.SpotifyPlayer();
        player["CanControl"] = false;

        // Act
        state.ApplyPlayerProperties(player, T0);

        // Assert
        state.Capabilities.Should().Be(LocalPlayerCapabilities.None);
    }

    [Fact]
    public void PausingFreezesTheExtrapolatedPositionAndAsksForARead()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);
        TimeSpan later = T0 + TimeSpan.FromSeconds(3);

        // Act
        bool needsRead = state.ApplyPlayerProperties(new Dictionary<string, object?> { ["PlaybackStatus"] = "Paused" }, later);

        // Assert
        needsRead.Should().BeTrue();
        LocalPlayerSnapshot snapshot = state.ToSnapshot();
        snapshot.Status.Should().Be(LocalPlayerStatus.Paused);
        snapshot.Position.Should().Be(TimeSpan.FromMilliseconds(115283));
        snapshot.ProjectPosition(later + TimeSpan.FromSeconds(10)).Should().Be(TimeSpan.FromMilliseconds(115283));
    }

    [Fact]
    public void ResumingRecordsWhenThePlayerStartedPlaying()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);
        state.ApplyPlayerProperties(new Dictionary<string, object?> { ["PlaybackStatus"] = "Paused" }, T0 + TimeSpan.FromSeconds(1));

        // Act
        state.ApplyPlayerProperties(new Dictionary<string, object?> { ["PlaybackStatus"] = "Playing" }, T0 + TimeSpan.FromSeconds(9));

        // Assert
        state.LastStartedPlayingAt.Should().Be(T0 + TimeSpan.FromSeconds(9));
    }

    [Fact]
    public void ANewTrackRestartsThePositionAndAsksForARead()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata["xesam:title"] = "Next Song";
        metadata["mpris:trackid"] = "/com/spotify/track/2mlNgAeIBnL78ZriXgrRHz";

        // Act
        bool needsRead = state.ApplyPlayerProperties(new Dictionary<string, object?> { ["Metadata"] = metadata }, T0 + TimeSpan.FromSeconds(2));

        // Assert
        needsRead.Should().BeTrue();
        state.Track!.Title.Should().Be("Next Song");
        state.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TheSameMetadataAgainChangesNothing()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);

        // Act
        bool needsRead = state.ApplyPlayerProperties(
            new Dictionary<string, object?> { ["Metadata"] = MprisHostMaps.SpotifyMetadata() },
            T0 + TimeSpan.FromSeconds(2));

        // Assert
        needsRead.Should().BeFalse();
        state.Position.Should().Be(TimeSpan.FromMilliseconds(112283));
    }

    [Theory]
    [InlineData("mpris:artUrl", "https://i.scdn.co/image/late-artwork")]
    [InlineData("xesam:album", "Reflections (Deluxe)")]
    [InlineData("mpris:length", 200_000_000L)]
    public void AMetadataRefreshForTheSameTrack_KeepsTheTimeline(string key, object value)
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);
        int timeline = state.TimelineGeneration;
        LocalTrack before = state.Track!;
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata[key] = value;

        // Act
        bool needsRead = state.ApplyPlayerProperties(
            new Dictionary<string, object?> { ["Metadata"] = metadata },
            T0 + TimeSpan.FromSeconds(2));

        // Assert
        needsRead.Should().BeFalse();
        state.Track.Should().NotBe(before, "the refreshed details are kept");
        state.ToSnapshot().ProjectPosition(T0 + TimeSpan.FromSeconds(2))
            .Should().Be(TimeSpan.FromMilliseconds(112283 + 2000));
        state.TimelineGeneration.Should().Be(timeline);
    }

    [Fact]
    public void ADifferentAlbumWithoutATrackId_AsksForAPositionRead()
    {
        // Arrange
        Dictionary<string, object?> player = MprisHostMaps.SpotifyPlayer();
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata.Remove("mpris:trackid");
        player["Metadata"] = metadata;
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(player, T0);
        Dictionary<string, object?> liveVersion = MprisHostMaps.SpotifyMetadata();
        liveVersion.Remove("mpris:trackid");
        liveVersion["xesam:album"] = "Reflections (Live)";

        // Act
        bool needsRead = state.ApplyPlayerProperties(
            new Dictionary<string, object?> { ["Metadata"] = liveVersion },
            T0 + TimeSpan.FromSeconds(2));

        // Assert
        needsRead.Should().BeTrue("without a trackid the same title and artist may be the next track");
        state.Track!.Album.Should().Be("Reflections (Live)");
    }

    [Fact]
    public void Seeked_SetsThePositionAndCountsTheSeek()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);

        // Act
        state.ApplySeeked(30_000_000, T0 + TimeSpan.FromSeconds(1));

        // Assert
        state.Position.Should().Be(TimeSpan.FromSeconds(30));
        state.PositionCapturedAt.Should().Be(T0 + TimeSpan.FromSeconds(1));
        state.SeekGeneration.Should().Be(1);
    }

    [Fact]
    public void TimelineGeneration_MovesOnTrackStatusAndSeekChangesButNotOnAPositionRead()
    {
        // Arrange
        MprisPlayerState state = new(MprisHostMaps.SPOTIFY_BUS_NAME, ":1.1");
        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0);
        int initial = state.TimelineGeneration;
        Dictionary<string, object?> metadata = MprisHostMaps.SpotifyMetadata();
        metadata["xesam:title"] = "Next Song";
        metadata["mpris:trackid"] = "/com/spotify/track/2mlNgAeIBnL78ZriXgrRHz";

        // Act & Assert
        state.SetPosition(5_000_000, T0 + TimeSpan.FromSeconds(1));
        state.TimelineGeneration.Should().Be(initial, "a read answers for the current timeline");

        state.ApplyPlayerProperties(MprisHostMaps.SpotifyPlayer(), T0 + TimeSpan.FromSeconds(1));
        state.TimelineGeneration.Should().Be(initial + 1, "GetAll carried a position");

        state.ApplyPlayerProperties(new Dictionary<string, object?> { ["Metadata"] = metadata }, T0 + TimeSpan.FromSeconds(2));
        state.TimelineGeneration.Should().Be(initial + 2);

        state.ApplyPlayerProperties(new Dictionary<string, object?> { ["PlaybackStatus"] = "Paused" }, T0 + TimeSpan.FromSeconds(3));
        state.TimelineGeneration.Should().Be(initial + 3);

        state.ApplySeeked(30_000_000, T0 + TimeSpan.FromSeconds(4));
        state.TimelineGeneration.Should().Be(initial + 4);

        state.ApplyPlayerProperties(new Dictionary<string, object?> { ["CanSeek"] = false }, T0 + TimeSpan.FromSeconds(5));
        state.TimelineGeneration.Should().Be(initial + 4, "a capability change leaves the timeline alone");
    }

    [Fact]
    public void ProjectPosition_ExtrapolatesWithTheRateAndStopsAtTheEnd()
    {
        // Arrange
        LocalPlayerSnapshot playing = LocalPlayers.Player(
            "p",
            LocalPlayerStatus.Playing,
            LocalPlayers.Song(seconds: 60),
            position: TimeSpan.FromSeconds(10),
            capturedAt: T0) with
        { Rate = 1.5 };

        // Act & Assert
        playing.ProjectPosition(T0 + TimeSpan.FromSeconds(4)).Should().Be(TimeSpan.FromSeconds(16));
        playing.ProjectPosition(T0 + TimeSpan.FromMinutes(5)).Should().Be(TimeSpan.FromSeconds(60));
        playing.ProjectPosition(T0 - TimeSpan.FromSeconds(1)).Should().Be(TimeSpan.FromSeconds(10));
    }
}
