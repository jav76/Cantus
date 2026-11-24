using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.Tests.LocalPlayback;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

/// <summary>The copy that changes with the Session kind: the no-track hint, the empty stage and the device name.</summary>
public sealed class LyricsViewModelLocalSessionTests
{
    private readonly SignalRPlaybackClient _client = new("http://127.0.0.1:59999/hubs/playback");
    private readonly Mock<ISpotifySetupApi> _setupApi = new();
    private readonly FakeLocalPlayback _local = new();
    private bool _serverAllowsLocal = true;

    public LyricsViewModelLocalSessionTests()
    {
        _setupApi
            .Setup(a => a.GetSetupInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SpotifySetupInfoPayload { LocalSessionsEnabled = _serverAllowsLocal });
    }

    [Fact]
    public async Task SignedOutWithThisComputerOffered_SuggestsBothSources()
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel();

        // Act
        await vm.Session.RefreshLocalAvailabilityAsync();

        // Assert
        vm.CurrentArtist.Should().Be(LyricsViewModel.SIGNED_OUT_LOCAL_HINT);
        vm.EmptyStateSubtitle.Should().Be($"{LyricsViewModel.SIGNED_OUT_LOCAL_HINT}.");
    }

    [Fact]
    public async Task SignedOutWithoutThisComputer_KeepsTheSpotifyWording()
    {
        // Arrange
        _serverAllowsLocal = false;
        LyricsViewModel vm = CreateViewModel();

        // Act
        await vm.Session.RefreshLocalAvailabilityAsync();

        // Assert
        vm.CurrentArtist.Should().Be(LyricsViewModel.SPOTIFY_NO_TRACK_HINT);
        vm.EmptyStateSubtitle.Should().Be(LyricsViewModel.SPOTIFY_EMPTY_STAGE_HINT);
    }

    [Fact]
    public async Task LocalSessionWithNothingPlaying_PointsAtThisComputersPlayers()
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel();
        await vm.Session.RefreshLocalAvailabilityAsync();

        // Act
        SignIn(AuthorizedSessionPayload.KIND_LOCAL);
        _client.RaisePlaybackStateReceived(new PlaybackStatePayload { Source = PlaybackStatePayload.SOURCE_LOCAL });

        // Assert
        vm.CurrentArtist.Should().Be(LyricsViewModel.LOCAL_NO_TRACK_HINT);
        vm.EmptyStateSubtitle.Should().Be($"{LyricsViewModel.LOCAL_NO_TRACK_HINT}.");
    }

    [Fact]
    public async Task SpotifySessionWithNothingPlaying_KeepsTheSpotifyWording()
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel();
        await vm.Session.RefreshLocalAvailabilityAsync();

        // Act
        SignIn(AuthorizedSessionPayload.KIND_SPOTIFY);
        _client.RaisePlaybackStateReceived(new PlaybackStatePayload());

        // Assert
        vm.CurrentArtist.Should().Be(LyricsViewModel.SPOTIFY_NO_TRACK_HINT);
        vm.EmptyStateSubtitle.Should().Be(LyricsViewModel.SPOTIFY_EMPTY_STAGE_HINT);
    }

    [Fact]
    public async Task SigningOutOfALocalSession_ReturnsToTheChoiceWording()
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel();
        await vm.Session.RefreshLocalAvailabilityAsync();
        SignIn(AuthorizedSessionPayload.KIND_LOCAL);
        vm.CurrentArtist.Should().Be(LyricsViewModel.LOCAL_NO_TRACK_HINT);

        // Act
        await vm.Session.LogoutAsync();

        // Assert
        vm.CurrentArtist.Should().Be(LyricsViewModel.SIGNED_OUT_LOCAL_HINT);
        _local.CreatedWithJoinCodes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PlaybackStatePayload.SOURCE_LOCAL, null, LyricsViewModel.LOCAL_DEVICE_FALLBACK)]
    [InlineData(PlaybackStatePayload.SOURCE_LOCAL, "VLC media player", "VLC media player")]
    [InlineData(PlaybackStatePayload.SOURCE_SPOTIFY, null, LyricsViewModel.SPOTIFY_DEVICE_FALLBACK)]
    [InlineData(PlaybackStatePayload.SOURCE_SPOTIFY, "Kitchen", "Kitchen")]
    public void DeviceName_IsThePlayerNameAndNeverSpotifyForALocalPlayer(string source, string? deviceName, string expected)
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel();

        // Act
        _client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            Source = source,
            DeviceName = deviceName,
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist", DurationMs = 180000 },
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        // Assert
        vm.DeviceName.Should().Be(expected);
    }

    [Fact]
    public void DeviceName_ForALocalSessionWithoutASource_DoesNotSaySpotify()
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel();
        SignIn(AuthorizedSessionPayload.KIND_LOCAL);

        // Act: an older server that sends neither the source nor a device name.
        _client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist", DurationMs = 180000 },
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        // Assert
        vm.DeviceName.Should().Be(LyricsViewModel.LOCAL_DEVICE_FALLBACK);
    }

    private LyricsViewModel CreateViewModel() =>
        new(
            _client,
            new ThemeManager(),
            new ResponsiveLayoutManager(),
            new LyricsPreferences(new InMemorySettingsStore()),
            new Mock<IDisplayPairingApi>().Object,
            new DisplayCredentialStore(new InMemorySettingsStore()),
            displaysApi: new Mock<IDisplayManagementApi>().Object,
            pendingPairCodes: new PendingPairCodeStore(new InMemorySettingsStore()),
            localPlayback: _local,
            spotifySetupApi: _setupApi.Object);

    private void SignIn(string kind) =>
        _client.RaiseSessionsReceived(new List<AuthorizedSessionPayload>
        {
            new() { Id = "session-1", DisplayName = "Jaret-Desktop", Kind = kind },
        });
}
