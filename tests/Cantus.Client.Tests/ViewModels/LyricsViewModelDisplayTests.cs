using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelDisplayTests
{
    private const string TRACK_ID = "track-1";
    private const string TOKEN = "display-token";

    private readonly Mock<ILyricsCorrectionService> _lyricsCorrection = new();

    [Fact]
    public void PairedDisplay_CannotSeekByTappingALine()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: true);

        // Act
        LoadLyricsAndPlayback(client);

        // Assert
        vm.HasSyncedLyrics.Should().BeTrue();
        vm.CanSeekToLines.Should().BeFalse();
        vm.Pairing.OwnerControlsVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Owner_CanStillSeekByTappingALine()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: false);

        // Act
        LoadLyricsAndPlayback(client);

        // Assert
        vm.CanSeekToLines.Should().BeTrue();
    }

    [Fact]
    public void PairedDisplay_DefaultsFilmGrainOff_AndKeepsBackgroundBlur()
    {
        // Act
        (_, LyricsViewModel vm, _) = CreateViewModel(paired: true);

        // Assert
        vm.Effects.IsFilmGrainEnabled.Should().BeFalse();
        vm.Effects.IsBackgroundBlurEnabled.Should().BeTrue();
    }

    [Fact]
    public void DisplayRevokedEvent_RestoresTheOwnerFilmGrainDefault()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: true);

        // Act
        client.Raise(c => c.DisplayRevoked += null, "display-1");

        // Assert
        vm.Effects.IsFilmGrainEnabled.Should().BeTrue();
    }

    [Fact]
    public void DisplayRevokedEvent_ClearsPlaybackAndOpensThePairingScreen()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, InMemorySettingsStore store) =
            CreateViewModel(paired: true);
        LoadLyricsAndPlayback(client);

        // Act
        client.Raise(c => c.DisplayRevoked += null, "display-1");

        // Assert
        vm.Pairing.PairingScreenVisibility.Should().Be(Visibility.Visible);
        vm.Pairing.StatusMessage.Should().Be(DisplayPairingViewModel.REVOKED_MESSAGE);
        vm.LyricLines.Should().BeEmpty();
        vm.HasLyrics.Should().BeFalse();
        vm.CurrentTitle.Should().Be("No Track Playing");
        new DisplayCredentialStore(store).Load().Should().BeNull();
    }

    [Fact]
    public void EmptySessionListEvents_WhileADisplay_AreTreatedAsARejectedToken()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: true);

        // Act
        RaiseEmptySessions(client, DisplayPairingViewModel.MAX_TOKEN_REJECTIONS);

        // Assert
        vm.Pairing.IsDisplay.Should().BeFalse();
        vm.Pairing.StatusMessage.Should().Be(DisplayPairingViewModel.REJECTED_MESSAGE);
        vm.Session.IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void DiagnosticsEvent_ForAnAcceptedDisplay_ResetsTheRejectionCount()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: true);
        RaiseEmptySessions(client, DisplayPairingViewModel.MAX_TOKEN_REJECTIONS - 1);

        // Act
        client.Raise(c => c.DiagnosticsReceived += null, new DiagnosticsPayload { AuthorizedSessions = 1 });
        RaiseEmptySessions(client, 1);

        // Assert
        vm.Pairing.IsDisplay.Should().BeTrue();
    }

    [Fact]
    public async Task DisplayPairedEvent_SwitchesFromThePairingScreenToTheLyricsView()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, InMemorySettingsStore store) =
            CreateViewModel(paired: false);
        await vm.Pairing.StartPairingAsync();
        vm.Pairing.MainContentVisibility.Should().Be(Visibility.Collapsed);

        // Act
        client.Raise(c => c.DisplayPaired += null, new DisplayPairedPayload
        {
            PairingId = "pairing-1",
            DisplayId = "display-1",
            Name = "TV",
            Token = TOKEN,
        });

        // Assert
        vm.Pairing.IsDisplay.Should().BeTrue();
        vm.Pairing.MainContentVisibility.Should().Be(Visibility.Visible);
        vm.Pairing.PairingScreenVisibility.Should().Be(Visibility.Collapsed);
        new DisplayCredentialStore(store).Load()!.Token.Should().Be(TOKEN);
        client.Verify(c => c.ReconnectWithDisplayTokenAsync(TOKEN), Times.Once);
    }

    [Fact]
    public async Task PairedDisplay_HidesTheLyricsPickerAndRefusesToOpenIt()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: true);
        LoadLyricsAndPlayback(client);

        // Act
        await vm.LyricsPicker.OpenAsync();

        // Assert
        vm.LyricsPicker.IsAvailable.Should().BeFalse();
        vm.LyricsPicker.ActionVisibility.Should().Be(Visibility.Collapsed);
        vm.LyricsPicker.IsOpen.Should().BeFalse();
        _lyricsCorrection.Verify(
            s => s.GetCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PairingAfterSigningIn_ClosesTheLyricsPickerAndRefusesToReopenIt()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, LyricsViewModel vm, _) = CreateViewModel(paired: false);
        _lyricsCorrection
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsCandidatesResult(
                LyricsCorrectionStatus.Succeeded,
                new LyricsCandidatesPayload { TrackId = TRACK_ID }));
        client.Raise(c => c.SessionsReceived += null, (IReadOnlyList<AuthorizedSessionPayload>)new List<AuthorizedSessionPayload>
        {
            new() { Id = "sess-owner", DisplayName = "Owner" },
        });
        LoadLyricsAndPlayback(client);
        await vm.LyricsPicker.OpenAsync();
        vm.LyricsPicker.IsOpen.Should().BeTrue();

        // Act
        client.Raise(c => c.DisplayPaired += null, new DisplayPairedPayload
        {
            PairingId = "pairing-1",
            DisplayId = "display-1",
            Name = "TV",
            Token = TOKEN,
        });
        await vm.LyricsPicker.OpenAsync();

        // Assert
        vm.Pairing.IsDisplay.Should().BeTrue();
        vm.Session.CurrentUserSession.Should().NotBeNull("the stale Session list is what the Display check guards against");
        vm.LyricsPicker.IsAvailable.Should().BeFalse();
        vm.LyricsPicker.ActionVisibility.Should().Be(Visibility.Collapsed);
        vm.LyricsPicker.IsOpen.Should().BeFalse();
        _lyricsCorrection.Verify(
            s => s.GetCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private (Mock<ISignalRPlaybackClient> Client, LyricsViewModel Vm, InMemorySettingsStore Store)
        CreateViewModel(bool paired)
    {
        InMemorySettingsStore store = new();
        if (paired)
        {
            new DisplayCredentialStore(store).Save(new DisplayCredential("display-1", "TV", TOKEN));
        }

        Mock<ISignalRPlaybackClient> client = new();
        client.SetupProperty(c => c.DisplayToken);
        client.SetupGet(c => c.ServerBaseUrl).Returns("http://localhost:5000");
        client.SetupGet(c => c.State).Returns(HubConnectionState.Connected);
        client.SetupGet(c => c.ConnectionId).Returns("conn-1");
        client.Setup(c => c.ReconnectWithDisplayTokenAsync(It.IsAny<string?>())).Returns(Task.CompletedTask);
        client.Setup(c => c.SubscribeToUserAsync(It.IsAny<string?>())).Returns(Task.CompletedTask);

        Mock<IDisplayPairingApi> api = new();
        api
            .Setup(a => a.RequestPairingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayPairingRequestResult(
                DisplayPairingRequestStatus.Created,
                new DisplayPairingCodePayload
                {
                    PairingId = "pairing-1",
                    Code = "AAAABBBB",
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                }));

        Mock<IDisplayManagementApi> displaysApi = new();
        displaysApi
            .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayListResult(DisplayManagementStatus.Succeeded, new List<DisplayInfoPayload>()));

        LyricsViewModel vm = new(
            client.Object,
            new ThemeManager(),
            new ResponsiveLayoutManager(),
            new ClientPreferences(new InMemorySettingsStore()),
            api.Object,
            new DisplayCredentialStore(store),
            displaysApi: displaysApi.Object,
            lyricsCorrection: _lyricsCorrection.Object);
        return (client, vm, store);
    }

    private static void RaiseEmptySessions(Mock<ISignalRPlaybackClient> client, int times)
    {
        IReadOnlyList<AuthorizedSessionPayload> noSessions = new List<AuthorizedSessionPayload>();
        for (int i = 0; i < times; i++)
        {
            client.Raise(c => c.SessionsReceived += null, noSessions);
        }
    }

    private static void LoadLyricsAndPlayback(Mock<ISignalRPlaybackClient> client)
    {
        client.Raise(c => c.LyricsReceived += null, new LyricsPayload
        {
            TrackId = TRACK_ID,
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "one" },
                new() { TimestampMs = 5000, Text = "two" },
            },
        });
        client.Raise(c => c.PlaybackStateReceived += null, new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = TRACK_ID, Title = "Song", Artist = "Artist", DurationMs = 60000 },
            ProgressMs = 2000,
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }
}
