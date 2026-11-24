using System;
using Cantus.Client.LocalPlayback;
using FluentAssertions;
using Xunit;
using static Cantus.Client.Tests.LocalPlayback.LocalPlayers;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class PlayerSelectionPolicyTests
{
    private const string SPOTIFY = "org.mpris.MediaPlayer2.spotify";
    private const string BRAVE = "org.mpris.MediaPlayer2.brave.instance2";
    private const string CHROMIUM = "org.mpris.MediaPlayer2.chromium.instance4004144";

    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);

    private readonly PlayerSelectionPolicy _policy = new();

    [Fact]
    public void Select_FollowsThePlayingPlayerAtOnceWhenNothingIsFollowed()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0);

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([spotify], AllEnabled, T0);

        // Assert
        followed.Should().BeSameAs(spotify);
        _policy.FollowedInstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void Select_SwitchesToTheMostRecentlyStartedPlayerAfterTheDebounce()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0);
        _policy.Select([spotify], AllEnabled, T0);
        TimeSpan braveStart = T0 + TimeSpan.FromSeconds(30);
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Playing, Song("Other", "Band"), startedAt: braveStart);

        // Act
        LocalPlayerSnapshot? atStart = _policy.Select([spotify, brave], AllEnabled, braveStart);
        LocalPlayerSnapshot? justBefore = _policy.Select(
            [spotify, brave],
            AllEnabled,
            braveStart + PlayerSelectionPolicy.SWITCH_DEBOUNCE - TimeSpan.FromMilliseconds(1));
        LocalPlayerSnapshot? after = _policy.Select(
            [spotify, brave],
            AllEnabled,
            braveStart + PlayerSelectionPolicy.SWITCH_DEBOUNCE);

        // Assert
        atStart!.InstanceId.Should().Be(SPOTIFY);
        justBefore!.InstanceId.Should().Be(SPOTIFY);
        after!.InstanceId.Should().Be(BRAVE);
    }

    [Fact]
    public void Select_ABriefPlayElsewhereDoesNotSwitch()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0);
        _policy.Select([spotify], AllEnabled, T0);
        TimeSpan braveStart = T0 + TimeSpan.FromSeconds(30);
        LocalPlayerSnapshot bravePlaying = Player(BRAVE, LocalPlayerStatus.Playing, Song("Other", "Band"), startedAt: braveStart);
        LocalPlayerSnapshot bravePaused = bravePlaying with { Status = LocalPlayerStatus.Paused };

        // Act
        _policy.Select([spotify, bravePlaying], AllEnabled, braveStart);
        _policy.Select([spotify, bravePaused], AllEnabled, braveStart + TimeSpan.FromMilliseconds(600));
        LocalPlayerSnapshot? later = _policy.Select(
            [spotify, bravePaused],
            AllEnabled,
            braveStart + TimeSpan.FromSeconds(5));

        // Assert
        later!.InstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void Select_AShortClipNeverTakesOver()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0);
        _policy.Select([spotify], AllEnabled, T0);
        LocalPlayerSnapshot clip = Player(BRAVE, LocalPlayerStatus.Playing, Song("Clip", "Uploader", seconds: 10), startedAt: T0 + TimeSpan.FromSeconds(1));

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([spotify, clip], AllEnabled, T0 + TimeSpan.FromSeconds(10));

        // Assert
        followed!.InstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void Select_SkipsDisabledPlayers()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0, appId: "spotify");
        LocalPlayerSnapshot playerctld = Player(
            "org.mpris.MediaPlayer2.playerctld",
            LocalPlayerStatus.Playing,
            Song(),
            startedAt: T0 + TimeSpan.FromSeconds(1),
            appId: "playerctld");

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([spotify, playerctld], appId => appId != "playerctld", T0 + TimeSpan.FromSeconds(5));

        // Assert
        followed!.InstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void Select_DropsAFollowedPlayerThatIsSwitchedOff()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0, appId: "spotify");
        _policy.Select([spotify], AllEnabled, T0);

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([spotify], appId => appId != "spotify", T0 + TimeSpan.FromSeconds(1));

        // Assert
        followed.Should().BeNull();
    }

    [Fact]
    public void Select_KeepsTheCurrentPlayerWhenNothingIsPlaying()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0);
        _policy.Select([spotify], AllEnabled, T0);
        LocalPlayerSnapshot paused = spotify with { Status = LocalPlayerStatus.Paused };
        LocalPlayerSnapshot otherPaused = Player(BRAVE, LocalPlayerStatus.Paused, Song("Other", "Band"), startedAt: T0 + TimeSpan.FromSeconds(50));

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([paused, otherPaused], AllEnabled, T0 + TimeSpan.FromMinutes(10));

        // Assert
        followed!.InstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void Select_AnotherPlayerResumingOvertakesAPausedFollowedPlayer()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Paused, Song(), startedAt: T0 + TimeSpan.FromSeconds(10));
        _policy.Select([spotify], AllEnabled, T0 + TimeSpan.FromSeconds(20));
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Playing, Song("Other", "Band"), startedAt: T0);
        TimeSpan now = T0 + TimeSpan.FromSeconds(30);

        // Act
        _policy.Select([spotify, brave], AllEnabled, now);
        LocalPlayerSnapshot? followed = _policy.Select([spotify, brave], AllEnabled, now + PlayerSelectionPolicy.SWITCH_DEBOUNCE);

        // Assert
        followed!.InstanceId.Should().Be(BRAVE);
    }

    [Fact]
    public void Select_IgnoresIdleBrowserEntries()
    {
        // Arrange: what Brave and VS Code publish while idle.
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Stopped, track: null);
        LocalPlayerSnapshot chromium = Player(CHROMIUM, LocalPlayerStatus.Stopped, track: null);

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([brave, chromium], AllEnabled, T0);

        // Assert
        followed.Should().BeNull();
    }

    [Fact]
    public void Select_WithNothingPlayingAtFirst_FollowsAPausedPlayerWithASong()
    {
        // Arrange
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Stopped, track: null);
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Paused, Song());

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([brave, spotify], AllEnabled, T0);

        // Assert
        followed!.InstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void Select_WhenTheFollowedPlayerLeaves_SwitchesWithoutWaiting()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player(SPOTIFY, LocalPlayerStatus.Playing, Song(), startedAt: T0 + TimeSpan.FromSeconds(5));
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Playing, Song("Other", "Band"), startedAt: T0);
        _policy.Select([spotify, brave], AllEnabled, T0 + TimeSpan.FromSeconds(6));

        // Act
        LocalPlayerSnapshot? followed = _policy.Select([brave], AllEnabled, T0 + TimeSpan.FromSeconds(7));

        // Assert
        followed!.InstanceId.Should().Be(BRAVE);
    }

    private static bool AllEnabled(string appId) => true;
}
