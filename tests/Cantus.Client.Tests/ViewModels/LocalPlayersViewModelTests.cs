using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;
using Cantus.Client.Tests.LocalPlayback;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;
using static Cantus.Client.Tests.LocalPlayback.LocalPlayers;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LocalPlayersViewModelTests
{
    private readonly FakeLocalPlayback _local = new();
    private readonly LocalPlayersViewModel _players;

    public LocalPlayersViewModelTests()
    {
        _players = new LocalPlayersViewModel(_local);
    }

    [Fact]
    public void WhileInactive_ShowsAndListsNothing()
    {
        // Act
        _local.SetPlayers(Player("spotify", LocalPlayerStatus.Playing, Song()));

        // Assert
        _players.SectionVisibility.Should().Be(Visibility.Collapsed);
        _players.EmptyVisibility.Should().Be(Visibility.Collapsed);
        _players.Players.Should().BeEmpty();
    }

    [Fact]
    public void SetActive_ListsEachPlayerAppOnceWithItsSwitch()
    {
        // Arrange
        _local.SetPlayerEnabled("brave", false);
        _local.SetPlayers(
            Player("org.mpris.MediaPlayer2.spotify", LocalPlayerStatus.Playing, Song(), appId: "spotify"),
            Player("org.mpris.MediaPlayer2.brave.instance1", LocalPlayerStatus.Paused, Song(), appId: "brave"),
            Player("org.mpris.MediaPlayer2.brave.instance2", LocalPlayerStatus.Paused, Song(), appId: "brave"));

        // Act
        _players.SetActive(true);

        // Assert
        _players.SectionVisibility.Should().Be(Visibility.Visible);
        _players.EmptyVisibility.Should().Be(Visibility.Collapsed);
        _players.Players.Select(p => (p.AppId, p.IsEnabled)).Should().Equal(("spotify", true), ("brave", false));
    }

    [Fact]
    public void WithNoPlayers_ShowsTheEmptyText()
    {
        // Act
        _players.SetActive(true);

        // Assert
        _players.EmptyVisibility.Should().Be(Visibility.Visible);
        _players.EmptyText.Should().Be(LocalPlayersViewModel.EMPTY_TEXT);
    }

    [Fact]
    public void MarksTheFollowedPlayerAndMovesTheMarkerWhenItChanges()
    {
        // Arrange
        LocalPlayerSnapshot spotify = Player("spotify", LocalPlayerStatus.Playing, Song());
        LocalPlayerSnapshot vlc = Player("vlc", LocalPlayerStatus.Playing, Song());
        _local.SetPlayers(spotify, vlc);
        _players.SetActive(true);

        // Act
        _local.SetFollowed(spotify);

        // Assert
        _players.Players.Single(p => p.AppId == "spotify").FollowedVisibility.Should().Be(Visibility.Visible);
        _players.Players.Single(p => p.AppId == "vlc").IsFollowed.Should().BeFalse();

        // Act
        _local.SetFollowed(vlc);

        // Assert
        _players.Players.Single(p => p.AppId == "spotify").IsFollowed.Should().BeFalse();
        _players.Players.Single(p => p.AppId == "vlc").IsFollowed.Should().BeTrue();
    }

    [Fact]
    public void TogglingASwitch_SavesItForThatPlayer()
    {
        // Arrange
        _local.SetPlayers(Player("vlc", LocalPlayerStatus.Playing, Song()));
        _players.SetActive(true);

        // Act
        _players.Players.Single().IsEnabled = false;

        // Assert
        _local.Switches.Should().Equal(("vlc", false));
    }

    [Fact]
    public void APlayerThatCloses_StaysListedSoItsSwitchCanStillBeChanged()
    {
        // Arrange
        _local.SetPlayers(Player("vlc", LocalPlayerStatus.Playing, Song()));
        _players.SetActive(true);

        // Act
        _local.SetPlayers();

        // Assert
        _players.Players.Should().ContainSingle(p => p.AppId == "vlc");
    }

    [Fact]
    public async Task WithTheRealPublisher_TogglingWritesThePreferenceAndTheMarkerFollows()
    {
        // Arrange
        InMemorySettingsStore settings = new();
        FakeLocalPlayerSource source = new();
        LocalPlayerPreferences preferences = new(settings);
        LocalPlaybackPublisher publisher = new(
            source,
            new FakeLocalPlaybackSink(),
            new PlayerSelectionPolicy(),
            preferences,
            new FakeTimeProvider());
        Mock<ISignalRPlaybackClient> client = new();
        client.SetupGet(c => c.SessionToken).Returns("local-1");
        LocalPlaybackBootstrap bootstrap = new(
            client.Object,
            source,
            new Mock<ILocalSessionApi>().Object,
            publisher,
            preferences,
            "jaret-desktop",
            joinCodePrefill: null);
        source.SetPlayers(
            Player("spotify", LocalPlayerStatus.Playing, Song(), startedAt: TimeSpan.FromSeconds(1)),
            Player("brave", LocalPlayerStatus.Playing, Song("Clip"), startedAt: TimeSpan.FromSeconds(2)));
        await bootstrap.StartAsync();
        client.Raise(
            c => c.SessionsReceived += null,
            (IReadOnlyList<AuthorizedSessionPayload>)
            [new AuthorizedSessionPayload { Id = "local-1", Kind = AuthorizedSessionPayload.KIND_LOCAL }]);
        LocalPlayersViewModel players = new(bootstrap);
        players.SetActive(true);
        players.Players.Single(p => p.AppId == "brave").IsFollowed.Should().BeTrue();

        // Act
        players.Players.Single(p => p.AppId == "brave").IsEnabled = false;

        // Assert
        (settings.GetValue(LocalPlayerPreferences.KEY_DISABLED_PLAYERS) as string).Should().Contain("brave");
        players.Players.Single(p => p.AppId == "spotify").IsFollowed.Should().BeTrue();
        players.Players.Single(p => p.AppId == "brave").IsFollowed.Should().BeFalse();
        await bootstrap.DisposeAsync();
    }
}
