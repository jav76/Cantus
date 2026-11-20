using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Time.Testing;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class DisplayPairingViewModelTests
{
    private const string SERVER_BASE_URL = "http://192.168.0.10:5000";
    private const string CONNECTION_ID = "conn-1";
    private const string TOKEN = "display-token-secret";
    private static readonly TimeSpan CODE_LIFETIME = TimeSpan.FromMinutes(5);

    [Fact]
    public void NewClient_WithoutAToken_IsUnpairedWithOwnerControlsShown()
    {
        // Arrange & Act
        PairingFixture fixture = new();

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Unpaired);
        fixture.Pairing.IsDisplay.Should().BeFalse();
        fixture.Pairing.CanControlPlayback.Should().BeTrue();
        fixture.Pairing.OwnerControlsVisibility.Should().Be(Visibility.Visible);
        fixture.Pairing.DisplayInfoVisibility.Should().Be(Visibility.Collapsed);
        fixture.Pairing.PairingScreenVisibility.Should().Be(Visibility.Collapsed);
        fixture.Pairing.MainContentVisibility.Should().Be(Visibility.Visible);
        fixture.Client.Object.DisplayToken.Should().BeNull();
    }

    [Fact]
    public async Task StartPairing_WhenConnected_RequestsACodeAndShowsItWithAQr()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-1", "K7QH2MZP");

        // Act
        await fixture.Pairing.StartPairingAsync();

        // Assert
        fixture.Api.Verify(a => a.RequestPairingAsync(CONNECTION_ID, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.Code.Should().Be("K7QH2MZP");
        fixture.Pairing.CodeText.Should().Be("K7QH-2MZP");
        fixture.Pairing.ApprovalUrl.Should().Be("http://192.168.0.10:5000/?pair=K7QH2MZP");
        fixture.Pairing.QrCode.Should().NotBeNull();
        fixture.Pairing.ExpiresInText.Should().Be("Code expires in 5:00");
        fixture.Pairing.CodeVisibility.Should().Be(Visibility.Visible);
        fixture.Pairing.PairingScreenVisibility.Should().Be(Visibility.Visible);
        fixture.Pairing.MainContentVisibility.Should().Be(Visibility.Collapsed);
        fixture.Pairing.OwnerControlsVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task StartPairing_BeforeTheHubConnects_WaitsAndRequestsOnConnect()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.SetConnection(HubConnectionState.Disconnected, null);
        fixture.EnqueueCode("pairing-1", "K7QH2MZP");

        // Act
        await fixture.Pairing.StartPairingAsync();

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.RequestingCode);
        fixture.Pairing.PairingProgressText.Should().Be(DisplayPairingViewModel.WAITING_FOR_SERVER_TEXT);
        fixture.Api.Verify(
            a => a.RequestPairingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Act
        fixture.SetConnection(HubConnectionState.Connected, CONNECTION_ID);
        fixture.Pairing.ApplyConnectionState("Connected");

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.Code.Should().Be("K7QH2MZP");
    }

    [Fact]
    public async Task ShownCode_CountsDownAndRenewsWhenItExpires()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-1", "AAAABBBB");
        fixture.EnqueueCode("pairing-2", "CCCCDDDD", fixture.Time.GetUtcNow() + (CODE_LIFETIME * 2));
        await fixture.Pairing.StartPairingAsync();
        List<DisplayPairingState> states = new();
        fixture.Pairing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DisplayPairingViewModel.State))
            {
                states.Add(fixture.Pairing.State);
            }
        };

        // Act
        fixture.Time.Advance(TimeSpan.FromSeconds(61));

        // Assert
        fixture.Pairing.ExpiresInText.Should().Be("Code expires in 3:59");
        fixture.Pairing.Code.Should().Be("AAAABBBB");

        // Act
        fixture.Time.Advance(TimeSpan.FromSeconds(240));

        // Assert
        states.Should().ContainInOrder(DisplayPairingState.CodeExpired, DisplayPairingState.ShowingCode);
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.Code.Should().Be("CCCCDDDD");
        fixture.Pairing.ApprovalUrl.Should().EndWith("?pair=CCCCDDDD");
        fixture.Api.Verify(
            a => a.RequestPairingAsync(CONNECTION_ID, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ShownCode_UsesTheClockOffsetSoAFastDeviceClockDoesNotExpireItEarly()
    {
        // Arrange: the server clock is ten minutes behind this device.
        PairingFixture fixture = new();
        fixture.Client.SetupGet(c => c.ClockOffsetMs).Returns((long)TimeSpan.FromMinutes(-10).TotalMilliseconds);
        fixture.EnqueueCode(
            "pairing-1",
            "AAAABBBB",
            fixture.Time.GetUtcNow() - TimeSpan.FromMinutes(10) + CODE_LIFETIME);

        // Act
        await fixture.Pairing.StartPairingAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(4));

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.ExpiresInText.Should().Be("Code expires in 1:00");
    }

    [Fact]
    public async Task Reconnect_DropsTheCodeAndRequestsANewOneForTheNewConnection()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-1", "AAAABBBB");
        fixture.EnqueueCode("pairing-2", "CCCCDDDD");
        await fixture.Pairing.StartPairingAsync();

        // Act
        fixture.SetConnection(HubConnectionState.Reconnecting, null);
        fixture.Pairing.ApplyConnectionState("Reconnecting");

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.RequestingCode);
        fixture.Pairing.Code.Should().BeNull();
        fixture.Pairing.CodeVisibility.Should().Be(Visibility.Collapsed);

        // Act
        fixture.SetConnection(HubConnectionState.Connected, "conn-2");
        fixture.Pairing.ApplyConnectionState("Connected");

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.Code.Should().Be("CCCCDDDD");
        fixture.Api.Verify(a => a.RequestPairingAsync("conn-2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(DisplayPairingRequestStatus.RateLimited, DisplayPairingViewModel.RATE_LIMITED_MESSAGE, 60)]
    [InlineData(DisplayPairingRequestStatus.Unavailable, DisplayPairingViewModel.UNAVAILABLE_MESSAGE, 10)]
    [InlineData(DisplayPairingRequestStatus.Failed, DisplayPairingViewModel.FAILED_MESSAGE, 10)]
    public async Task FailedRequest_ShowsWhyAndRetriesLater(
        DisplayPairingRequestStatus status,
        string message,
        int retrySeconds)
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueResult(new DisplayPairingRequestResult(status));
        fixture.EnqueueCode("pairing-1", "AAAABBBB");

        // Act
        await fixture.Pairing.StartPairingAsync();

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.RequestFailed);
        fixture.Pairing.StatusMessage.Should().Be(message);
        fixture.Pairing.StatusMessageVisibility.Should().Be(Visibility.Visible);

        // Act
        fixture.Time.Advance(TimeSpan.FromSeconds(retrySeconds - 1));

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.RequestFailed);

        // Act
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.StatusMessage.Should().BeEmpty();
        fixture.Pairing.StatusMessageVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task CancelPairing_ClosesTheScreenStopsRenewingAndDropsTheConnectionsCode()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-1", "AAAABBBB");
        await fixture.Pairing.StartPairingAsync();

        // Act
        fixture.Pairing.CancelPairing();
        fixture.Time.Advance(CODE_LIFETIME * 2);

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Unpaired);
        fixture.Pairing.PairingScreenVisibility.Should().Be(Visibility.Collapsed);
        fixture.Pairing.Code.Should().BeNull();
        fixture.Api.Verify(
            a => a.RequestPairingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(null), Times.Once);
    }

    [Fact]
    public async Task DisplayPaired_StoresTheTokenReconnectsWithItAndHidesOwnerControls()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-1", "AAAABBBB");
        await fixture.Pairing.StartPairingAsync();

        // Act
        fixture.Pairing.ApplyDisplayPaired(Paired("pairing-1"));

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Paired);
        fixture.Pairing.IsDisplay.Should().BeTrue();
        fixture.Pairing.DisplayName.Should().Be("Living room TV");
        fixture.Pairing.DisplayId.Should().Be("display-1");
        fixture.Store.Values[DisplayCredentialStore.TOKEN_KEY].Should().Be(TOKEN);
        fixture.Store.Values[DisplayCredentialStore.DISPLAY_ID_KEY].Should().Be("display-1");
        fixture.Store.Values[DisplayCredentialStore.DISPLAY_NAME_KEY].Should().Be("Living room TV");
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(TOKEN), Times.Once);
        AssertDisplayVisibilities(fixture.Pairing);
    }

    [Fact]
    public async Task DisplayPaired_ForAnEarlierCodeOfThisConnection_IsKept()
    {
        // Arrange: the owner approved the previous code just as it was renewed.
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-2", "AAAABBBB");
        await fixture.Pairing.StartPairingAsync();

        // Act
        fixture.Pairing.ApplyDisplayPaired(Paired("pairing-1"));

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Paired);
        fixture.Store.Values[DisplayCredentialStore.TOKEN_KEY].Should().Be(TOKEN);
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(TOKEN), Times.Once);
    }

    [Fact]
    public async Task DisplayPaired_JustAfterCancel_IsKeptSoTheNewDisplayIsNotOrphaned()
    {
        // Arrange
        PairingFixture fixture = new();
        fixture.EnqueueCode("pairing-1", "AAAABBBB");
        await fixture.Pairing.StartPairingAsync();
        fixture.Pairing.CancelPairing();

        // Act
        fixture.Pairing.ApplyDisplayPaired(Paired("pairing-1"));

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Paired);
        new DisplayCredentialStore(fixture.Store).Load()!.Token.Should().Be(TOKEN);
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(TOKEN), Times.Once);
        AssertDisplayVisibilities(fixture.Pairing);
    }

    [Fact]
    public void DisplayPaired_WhenAlreadyADisplay_IsIgnored()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();

        // Act
        fixture.Pairing.ApplyDisplayPaired(new DisplayPairedPayload
        {
            PairingId = "pairing-2",
            DisplayId = "display-2",
            Name = "Other",
            Token = "other-token",
        });

        // Assert
        fixture.Pairing.DisplayId.Should().Be("display-1");
        new DisplayCredentialStore(fixture.Store).Load()!.Token.Should().Be(TOKEN);
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void DisplayPaired_WithoutAToken_IsIgnored()
    {
        // Arrange
        PairingFixture fixture = new();

        // Act
        fixture.Pairing.ApplyDisplayPaired(new DisplayPairedPayload { PairingId = "pairing-1", Token = " " });

        // Assert
        fixture.Pairing.IsDisplay.Should().BeFalse();
        fixture.Store.Values.Should().NotContainKey(DisplayCredentialStore.TOKEN_KEY);
    }

    [Fact]
    public void StoredToken_IsUsedAtStartupSoAPairedDisplayReconnectsAfterARestart()
    {
        // Arrange
        InMemorySettingsStore store = new();
        new DisplayCredentialStore(store).Save(new DisplayCredential("display-1", "Kitchen", TOKEN));

        // Act
        PairingFixture fixture = new(store);

        // Assert
        fixture.Client.Object.DisplayToken.Should().Be(TOKEN);
        fixture.Pairing.State.Should().Be(DisplayPairingState.Paired);
        fixture.Pairing.DisplayName.Should().Be("Kitchen");
        AssertDisplayVisibilities(fixture.Pairing);
    }

    [Fact]
    public void DisplayRevoked_DiscardsTheTokenAndReturnsToPairingWithAMessage()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();
        int endedCount = 0;
        fixture.Pairing.DisplayEnded += () => endedCount++;
        fixture.EnqueueCode("pairing-9", "EEEEFFFF");

        // Act
        fixture.Pairing.ApplyDisplayRevoked("display-1");

        // Assert
        fixture.Pairing.IsDisplay.Should().BeFalse();
        fixture.Pairing.PairingScreenVisibility.Should().Be(Visibility.Visible);
        fixture.Pairing.OwnerControlsVisibility.Should().Be(Visibility.Visible);
        fixture.Pairing.StatusMessage.Should().Be(DisplayPairingViewModel.REVOKED_MESSAGE);
        fixture.Store.Values[DisplayCredentialStore.TOKEN_KEY].Should().Be(string.Empty);
        new DisplayCredentialStore(fixture.Store).Load().Should().BeNull();
        fixture.Client.Object.DisplayToken.Should().BeNull();
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(null), Times.Once);
        endedCount.Should().Be(1);

        // Act: the fresh, unauthenticated connection comes up.
        fixture.Pairing.ApplyConnectionState("Connected");

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.ShowingCode);
        fixture.Pairing.Code.Should().Be("EEEEFFFF");
        fixture.Pairing.StatusMessage.Should().Be(DisplayPairingViewModel.REVOKED_MESSAGE);
    }

    [Fact]
    public void DisplayRevoked_ForAnotherDisplayIsIgnored()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();

        // Act
        fixture.Pairing.ApplyDisplayRevoked("display-other");

        // Assert
        fixture.Pairing.IsDisplay.Should().BeTrue();
        fixture.Client.Object.DisplayToken.Should().Be(TOKEN);
    }

    [Fact]
    public void EmptySessionList_WhileADisplay_KeepsTheTokenAndRetriesWithIt()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();

        // Act
        fixture.Pairing.ApplySessions(new List<AuthorizedSessionPayload>());

        // Assert
        fixture.Pairing.IsDisplay.Should().BeTrue();
        fixture.Pairing.IsPairingScreenOpen.Should().BeFalse();
        new DisplayCredentialStore(fixture.Store).Load()!.Token.Should().Be(TOKEN);
        fixture.Client.Object.DisplayToken.Should().Be(TOKEN);
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(It.IsAny<string?>()), Times.Never);

        // Act
        fixture.Time.Advance(TimeSpan.FromSeconds(15));

        // Assert
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(TOKEN), Times.Once);
    }

    [Fact]
    public void EmptySessionList_RepeatedWhileADisplay_MeansTheTokenWasRejected()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();
        int endedCount = 0;
        fixture.Pairing.DisplayEnded += () => endedCount++;

        // Act
        RejectToken(fixture.Pairing, DisplayPairingViewModel.MAX_TOKEN_REJECTIONS);

        // Assert
        fixture.Pairing.IsDisplay.Should().BeFalse();
        fixture.Pairing.IsPairingScreenOpen.Should().BeTrue();
        fixture.Pairing.StatusMessage.Should().Be(DisplayPairingViewModel.REJECTED_MESSAGE);
        new DisplayCredentialStore(fixture.Store).Load().Should().BeNull();
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(null), Times.Once);
        endedCount.Should().Be(1);

        // Act: no pending retry reconnects with the discarded token.
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        // Assert
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(TOKEN), Times.Never);
    }

    [Fact]
    public void AcceptedConnection_BetweenEmptySessionLists_StartsTheCountAgain()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();
        RejectToken(fixture.Pairing, DisplayPairingViewModel.MAX_TOKEN_REJECTIONS - 1);

        // Act
        fixture.Pairing.ApplyDiagnostics(new DiagnosticsPayload { AuthorizedSessions = 1 });
        RejectToken(fixture.Pairing, 1);

        // Assert
        fixture.Pairing.IsDisplay.Should().BeTrue();
        new DisplayCredentialStore(fixture.Store).Load()!.Token.Should().Be(TOKEN);
    }

    [Fact]
    public void UnauthorizedDiagnostics_DoNotCountAsAnAcceptedConnection()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();
        RejectToken(fixture.Pairing, DisplayPairingViewModel.MAX_TOKEN_REJECTIONS - 1);

        // Act
        fixture.Pairing.ApplyDiagnostics(new DiagnosticsPayload { AuthorizedSessions = 0 });
        RejectToken(fixture.Pairing, 1);

        // Assert
        fixture.Pairing.IsDisplay.Should().BeFalse();
        fixture.Pairing.StatusMessage.Should().Be(DisplayPairingViewModel.REJECTED_MESSAGE);
    }

    [Fact]
    public void AcceptedConnection_CancelsAPendingRetry()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();
        RejectToken(fixture.Pairing, 1);

        // Act: an automatic reconnect got through before the retry was due.
        fixture.Pairing.ApplyDiagnostics(new DiagnosticsPayload { AuthorizedSessions = 1 });
        fixture.Time.Advance(TimeSpan.FromMinutes(1));

        // Assert
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void OwnerSession_WhileADisplay_DropsTheDisplayTokenQuietly()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();

        // Act
        fixture.Pairing.ApplySessions(new List<AuthorizedSessionPayload>
        {
            new() { Id = "user-1", DisplayName = "Alice" },
        });

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Unpaired);
        fixture.Pairing.PairingScreenVisibility.Should().Be(Visibility.Collapsed);
        fixture.Pairing.StatusMessage.Should().BeEmpty();
        new DisplayCredentialStore(fixture.Store).Load().Should().BeNull();
    }

    [Fact]
    public void SessionList_WhenNotADisplay_ChangesNothing()
    {
        // Arrange
        PairingFixture fixture = new();

        // Act
        fixture.Pairing.ApplySessions(new List<AuthorizedSessionPayload>());

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Unpaired);
        fixture.Client.Verify(c => c.ReconnectWithDisplayTokenAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task StartPairing_WhenAlreadyADisplay_DoesNothing()
    {
        // Arrange
        PairingFixture fixture = PairedFixture();

        // Act
        await fixture.Pairing.StartPairingAsync();

        // Assert
        fixture.Pairing.State.Should().Be(DisplayPairingState.Paired);
        fixture.Api.Verify(
            a => a.RequestPairingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("K7QH2MZP", "K7QH-2MZP")]
    [InlineData("ABCDEFG", "ABCD-EFG")]
    [InlineData("ABCD", "ABCD")]
    [InlineData(" ABCDEF ", "ABC-DEF")]
    [InlineData("", "")]
    public void FormatCode_SplitsLongCodesIntoTwoGroups(string code, string expected)
    {
        // Act & Assert
        DisplayPairingViewModel.FormatCode(code).Should().Be(expected);
    }

    [Fact]
    public void BuildApprovalUrl_PutsTheCodeInThePairQueryParameter()
    {
        // Act & Assert
        DisplayPairingViewModel.BuildApprovalUrl("https://cantus.example/", "K7QH2MZP")
            .Should().Be("https://cantus.example/?pair=K7QH2MZP");
    }

    private static void AssertDisplayVisibilities(DisplayPairingViewModel pairing)
    {
        pairing.CanControlPlayback.Should().BeFalse();
        pairing.OwnerControlsVisibility.Should().Be(Visibility.Collapsed);
        pairing.DisplayInfoVisibility.Should().Be(Visibility.Visible);
        pairing.PairingScreenVisibility.Should().Be(Visibility.Collapsed);
        pairing.MainContentVisibility.Should().Be(Visibility.Visible);
    }

    private static void RejectToken(DisplayPairingViewModel pairing, int times)
    {
        for (int i = 0; i < times; i++)
        {
            pairing.ApplySessions(new List<AuthorizedSessionPayload>());
        }
    }

    private static PairingFixture PairedFixture()
    {
        InMemorySettingsStore store = new();
        new DisplayCredentialStore(store).Save(new DisplayCredential("display-1", "Living room TV", TOKEN));
        return new PairingFixture(store);
    }

    private static DisplayPairedPayload Paired(string pairingId) => new()
    {
        PairingId = pairingId,
        DisplayId = "display-1",
        Name = "Living room TV",
        Token = TOKEN,
    };

    private sealed class PairingFixture
    {
        private readonly Queue<DisplayPairingRequestResult> _results = new();
        private HubConnectionState _state = HubConnectionState.Connected;
        private string? _connectionId = CONNECTION_ID;

        public PairingFixture(InMemorySettingsStore? store = null)
        {
            Store = store ?? new InMemorySettingsStore();
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero));

            Client.SetupProperty(c => c.DisplayToken);
            Client.SetupGet(c => c.ServerBaseUrl).Returns(SERVER_BASE_URL);
            Client.SetupGet(c => c.State).Returns(() => _state);
            Client.SetupGet(c => c.ConnectionId).Returns(() => _connectionId);
            Client
                .Setup(c => c.ReconnectWithDisplayTokenAsync(It.IsAny<string?>()))
                .Callback<string?>(token => Client.Object.DisplayToken = token)
                .Returns(Task.CompletedTask);

            Api
                .Setup(a => a.RequestPairingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _results.Count > 0
                    ? _results.Dequeue()
                    : new DisplayPairingRequestResult(DisplayPairingRequestStatus.Failed));

            Pairing = new DisplayPairingViewModel(
                Client.Object,
                Api.Object,
                new DisplayCredentialStore(Store),
                Time);
        }

        public Mock<ISignalRPlaybackClient> Client { get; } = new();

        public Mock<IDisplayPairingApi> Api { get; } = new();

        public InMemorySettingsStore Store { get; }

        public FakeTimeProvider Time { get; }

        public DisplayPairingViewModel Pairing { get; }

        public void SetConnection(HubConnectionState state, string? connectionId)
        {
            _state = state;
            _connectionId = connectionId;
        }

        public void EnqueueCode(string pairingId, string code, DateTimeOffset? expiresAtUtc = null)
        {
            EnqueueResult(new DisplayPairingRequestResult(
                DisplayPairingRequestStatus.Created,
                new DisplayPairingCodePayload
                {
                    PairingId = pairingId,
                    Code = code,
                    ExpiresAtUtc = expiresAtUtc ?? Time.GetUtcNow() + CODE_LIFETIME,
                }));
        }

        public void EnqueueResult(DisplayPairingRequestResult result)
        {
            _results.Enqueue(result);
        }
    }
}
