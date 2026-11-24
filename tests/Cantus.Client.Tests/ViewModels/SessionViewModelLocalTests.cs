using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Icons;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;
using Cantus.Client.Tests.LocalPlayback;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;
using static Cantus.Client.Tests.LocalPlayback.LocalPlayers;

namespace Cantus.Client.Tests.ViewModels;

/// <summary>The Local Session side of <see cref="SessionViewModel"/>: the source choice, the account menu and switching.</summary>
public sealed class SessionViewModelLocalTests
{
    private const string MACHINE_NAME = "Jaret-Desktop";

    private readonly Mock<ISignalRPlaybackClient> _client = new();
    private readonly Mock<ISpotifySetupApi> _setupApi = new();
    private readonly FakeLocalPlayback _local = new();
    private readonly List<string> _calls = new();
    private SpotifySetupInfoPayload? _info = Info(localEnabled: true);

    public SessionViewModelLocalTests()
    {
        _client.Setup(c => c.SubscribeToUserAsync(It.IsAny<string?>())).Returns(Task.CompletedTask);
        _client.Setup(c => c.LogoutAsync()).Callback(() => _calls.Add("logout")).Returns(Task.CompletedTask);
        _setupApi
            .Setup(a => a.GetSetupInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _info);
        _local.OnCreate = () => _calls.Add("create");
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task IsLocalAvailable_NeedsASupportedSourceAndAServerThatAllowsIt(
        bool sourceSupported,
        bool serverEnabled,
        bool expected)
    {
        // Arrange
        _local.IsSupported = sourceSupported;
        _info = Info(localEnabled: serverEnabled);
        SessionViewModel session = CreateSession();

        // Act
        await session.RefreshLocalAvailabilityAsync();

        // Assert
        session.IsLocalAvailable.Should().Be(expected);
        session.LocalChoiceVisibility.Should().Be(expected ? Visibility.Visible : Visibility.Collapsed);
    }

    [Fact]
    public async Task IsLocalAvailable_WithoutALocalSource_StaysOffAndAsksTheServerNothing()
    {
        // Arrange: WASM, Windows and macOS have no local source.
        SessionViewModel session = new(
            _client.Object,
            new Mock<IDisplayManagementApi>().Object,
            spotifySetupApi: _setupApi.Object,
            ownSpotifyApp: new OwnSpotifyAppStore(new InMemorySettingsStore()));

        // Act
        await session.RefreshLocalAvailabilityAsync();
        _client.Raise(c => c.Connected += null);

        // Assert
        session.IsLocalAvailable.Should().BeFalse();
        session.LocalChoiceVisibility.Should().Be(Visibility.Collapsed);
        _setupApi.Verify(a => a.GetSetupInfoAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void IsLocalAvailable_IsCheckedAgainWhenTheSourceStartsAndOnEachConnect()
    {
        // Arrange
        _local.IsSupported = false;
        SessionViewModel session = CreateSession();
        session.IsLocalAvailable.Should().BeFalse();

        // Act
        _local.IsSupported = true;
        _local.RaiseAvailabilityChanged();

        // Assert
        session.IsLocalAvailable.Should().BeTrue();

        // Act: the server turns Local Sessions off and the client reconnects.
        _info = Info(localEnabled: false);
        _client.Raise(c => c.Connected += null);

        // Assert
        session.IsLocalAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task IsLocalAvailable_StaysOnWhenTheSetupInfoCannotBeReadOnAReconnect()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.IsLocalAvailable.Should().BeTrue();

        // Act
        _info = null;
        _client.Raise(c => c.Connected += null);
        await session.RefreshLocalAvailabilityAsync();

        // Assert
        session.IsLocalAvailable.Should().BeTrue();
        session.LocalChoiceVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task LocalJoinCodeRequired_FollowsTheSetupInfo()
    {
        // Arrange
        _info = Info(localEnabled: true, joinCodeRequired: true);
        _local.JoinCodePrefill = "PREFILLED1";
        SessionViewModel session = CreateSession();

        // Act
        await session.RefreshLocalAvailabilityAsync();

        // Assert
        session.LocalJoinCodeRequired.Should().BeTrue();
        session.JoinCodePrefill.Should().Be("PREFILLED1");
    }

    [Fact]
    public async Task LocalChoice_ShowsOnlyWhileSignedOut()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.LocalChoiceVisibility.Should().Be(Visibility.Visible);

        // Act & Assert
        session.ApplySessions([Spotify()]);
        session.LocalChoiceVisibility.Should().Be(Visibility.Collapsed);

        session.ApplySessions([Local()]);
        session.LocalChoiceVisibility.Should().Be(Visibility.Collapsed);

        session.ApplySessionRevoked(null);
        session.LocalChoiceVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task AccountMenu_WhenSignedOut_IsEmptySoTheButtonStartsTheSpotifySignIn()
    {
        // Arrange & Act
        SessionViewModel session = await CreateAvailableSessionAsync();

        // Assert
        session.HasAccountMenu.Should().BeFalse();
        session.AccountMenuItems.Should().BeEmpty();
        session.ConnectButtonText.Should().Be("Connect Spotify");
    }

    [Fact]
    public async Task AccountMenu_ForALocalSession_NamesTheComputerAndTheFollowedPlayer()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.ApplySessions([Local()]);

        // Act
        _local.SetFollowed(Player("spotify", LocalPlayerStatus.Playing, Song(), appId: "Spotify"));

        // Assert
        session.HasAccountMenu.Should().BeTrue();
        session.AccountMenuItems.Should().Equal(
            AccountMenuItem.Header($"This computer: {MACHINE_NAME}"),
            AccountMenuItem.Header("Following: Spotify"),
            AccountMenuItem.Separator,
            AccountMenuItem.Command("Connect Spotify instead", AccountMenuAction.ConnectSpotifyInstead),
            AccountMenuItem.Command("Sign out", AccountMenuAction.SignOut));
    }

    [Fact]
    public async Task AccountMenu_ForALocalSessionWithNothingPlaying_SaysSo()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();

        // Act
        session.ApplySessions([Local()]);

        // Assert
        session.FollowingText.Should().Be("No player playing");
        session.AccountMenuItems[1].Should().Be(AccountMenuItem.Header("No player playing"));
    }

    [Fact]
    public async Task AccountMenu_ForASpotifySession_OffersThisComputerWhenAvailable()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();

        // Act
        session.ApplySessions([Spotify()]);

        // Assert
        session.AccountMenuItems.Should().Equal(
            AccountMenuItem.Header("Spotify: Alice"),
            AccountMenuItem.Separator,
            AccountMenuItem.Command("Use this computer's players instead", AccountMenuAction.UseLocalPlayersInstead),
            AccountMenuItem.Command("Sign out", AccountMenuAction.SignOut));
    }

    [Fact]
    public async Task AccountMenu_ForASpotifySession_LeavesThisComputerOutWhenUnavailable()
    {
        // Arrange
        _info = Info(localEnabled: false);
        SessionViewModel session = CreateSession();
        await session.RefreshLocalAvailabilityAsync();

        // Act
        session.ApplySessions([Spotify()]);

        // Assert
        session.AccountMenuItems.Should().Equal(
            AccountMenuItem.Header("Spotify: Alice"),
            AccountMenuItem.Separator,
            AccountMenuItem.Command("Sign out", AccountMenuAction.SignOut));
    }

    [Fact]
    public async Task FollowedPlayerChanged_NotifiesTheFollowingText()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.ApplySessions([Local()]);
        List<string?> raised = new();
        session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        _local.SetFollowed(Player("vlc", LocalPlayerStatus.Playing, Song(), appId: "VLC"));

        // Assert
        session.FollowedPlayerName.Should().Be("VLC");
        raised.Should().Contain(new[]
        {
            nameof(SessionViewModel.FollowingText),
            nameof(SessionViewModel.AccountMenuItems),
        });
    }

    [Fact]
    public async Task SwitchToSpotifyAsync_SignsOutOfTheLocalSessionThenStartsTheSpotifySignIn()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.ApplySessions([Local()]);

        // Act
        await session.SwitchToSpotifyAsync(() =>
        {
            _calls.Add("spotify sign-in");
            return Task.CompletedTask;
        });

        // Assert
        _calls.Should().Equal("logout", "spotify sign-in");
        _local.SignedOutCount.Should().Be(1);
        _local.CreatedWithJoinCodes.Should().BeEmpty();
        session.CurrentUserSession.Should().BeNull();
    }

    [Fact]
    public async Task UseLocalPlayersAsync_FromASpotifySession_CreatesTheLocalSessionThenSignsOut()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.ApplySessions([Spotify()]);
        _local.EnqueueResults(LocalSessionStatus.Created);

        // Act
        LocalSessionResult result = await session.UseLocalPlayersAsync(null);

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Created);
        _calls.Should().Equal("create", "logout");
        _local.SignedOutCount.Should().Be(1);
    }

    [Theory]
    [InlineData(LocalSessionStatus.RateLimited)]
    [InlineData(LocalSessionStatus.LimitReached)]
    [InlineData(LocalSessionStatus.JoinCodeInvalid)]
    [InlineData(LocalSessionStatus.Failed)]
    public async Task UseLocalPlayersAsync_FromASpotifySession_WhenRefused_KeepsTheSpotifySession(LocalSessionStatus status)
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.ApplySessions([Spotify()]);
        _local.EnqueueResults(status);

        // Act
        LocalSessionResult result = await session.UseLocalPlayersAsync(null);

        // Assert
        result.Status.Should().Be(status);
        _calls.Should().Equal("create");
        _local.SignedOutCount.Should().Be(0);
        session.CurrentUserSession!.Id.Should().Be("user-1");
    }

    [Fact]
    public async Task UseLocalPlayers_WhileAnAttemptIsRunning_DoesNotStartASecond()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        _local.EnqueueResults(LocalSessionStatus.Created);
        _local.Gate = new TaskCompletionSource();
        session.LocalSetup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        session.CanUseLocalPlayers.Should().BeTrue();

        // Act
        Task<bool> first = session.LocalSetup.ConnectAsync();
        session.LocalSetup.Reset(joinCodeRequired: false, joinCodePrefill: null);
        bool second = await session.LocalSetup.ConnectAsync();

        // Assert
        session.CanUseLocalPlayers.Should().BeFalse();
        second.Should().BeFalse();
        session.LocalSetup.IsBusy.Should().BeTrue("the second click must not clear the running attempt");

        // Act
        _local.Gate.SetResult();

        // Assert
        (await first).Should().BeTrue();
        _local.CreatedWithJoinCodes.Should().HaveCount(1);
        session.CanUseLocalPlayers.Should().BeTrue();
        session.LocalSetup.ErrorVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task UseLocalPlayersAsync_WhenSignedOut_OnlyCreates()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        _local.EnqueueResults(LocalSessionStatus.Created);

        // Act
        await session.UseLocalPlayersAsync("JOINCODE1");

        // Assert
        _calls.Should().Equal("create");
        _local.CreatedWithJoinCodes.Should().Equal("JOINCODE1");
    }

    [Fact]
    public async Task UseLocalPlayersAsync_WithoutALocalSource_FailsWithoutSigningOut()
    {
        // Arrange
        SessionViewModel session = new(
            _client.Object,
            new Mock<IDisplayManagementApi>().Object,
            spotifySetupApi: _setupApi.Object,
            ownSpotifyApp: new OwnSpotifyAppStore(new InMemorySettingsStore()));
        session.ApplySessions([Spotify()]);

        // Act
        LocalSessionResult result = await session.UseLocalPlayersAsync(null);

        // Assert
        result.Status.Should().Be(LocalSessionStatus.Failed);
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LogoutAsync_FromALocalSession_ReturnsToTheChoiceWithoutCreatingAnother()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.ApplySessions([Local()]);

        // Act
        await session.LogoutAsync();
        _client.Raise(c => c.Connected += null);

        // Assert
        _local.SignedOutCount.Should().Be(1);
        _local.CreatedWithJoinCodes.Should().BeEmpty();
        session.HasAccountMenu.Should().BeFalse();
        session.IsLocalSession.Should().BeFalse();
        session.LocalChoiceVisibility.Should().Be(Visibility.Visible);
        session.ConnectButtonText.Should().Be("Connect Spotify");
    }

    [Fact]
    public async Task Copy_ForALocalSession_ReadsAsThisComputer()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();

        // Act
        session.ApplySessions([Local()]);

        // Assert
        session.IsLocalSession.Should().BeTrue();
        session.ConnectButtonText.Should().Be("This computer");
        session.ConnectButtonIcon.Should().Be(CantusIconKind.Display);
        session.AccountButtonToolTip.Should().Be($"This computer: {MACHINE_NAME}");
        session.AccountSourceText.Should().Be("This computer");
        session.AccountIcon.Should().Be(CantusIconKind.Display);
    }

    [Fact]
    public async Task Copy_ForASpotifySession_IsUnchangedApartFromTheSource()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();

        // Act
        session.ApplySessions([Spotify()]);

        // Assert
        session.IsLocalSession.Should().BeFalse();
        session.ConnectButtonText.Should().Be("Connected: Alice");
        session.ConnectButtonIcon.Should().Be(CantusIconKind.SpotifyConnected);
        session.AccountButtonToolTip.Should().Be("Connected: Alice");
        session.AccountSourceText.Should().Be("Spotify");
        session.AccountIcon.Should().Be(CantusIconKind.Account);
    }

    [Fact]
    public async Task SpotifyOnlyControls_AreHiddenForALocalSession()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();
        session.SongLookupVisibility.Should().Be(Visibility.Collapsed, "signed out");

        // Act & Assert
        session.ApplySessions([Spotify()]);
        session.SpotifyControlsVisibility.Should().Be(Visibility.Visible);
        session.SongLookupVisibility.Should().Be(Visibility.Visible);

        session.ApplySessions([Local()]);
        session.SpotifyControlsVisibility.Should().Be(Visibility.Collapsed);
        session.SongLookupVisibility.Should().Be(Visibility.Collapsed);
        session.OwnerActionsVisibility.Should().Be(Visibility.Visible, "other owner actions stay");
    }

    [Fact]
    public async Task LocalPlayers_IsActiveOnlyForALocalSession()
    {
        // Arrange
        SessionViewModel session = await CreateAvailableSessionAsync();

        // Act & Assert
        session.LocalPlayers.SectionVisibility.Should().Be(Visibility.Collapsed);

        session.ApplySessions([Local()]);
        session.LocalPlayers.SectionVisibility.Should().Be(Visibility.Visible);

        session.ApplySessions([Spotify()]);
        session.LocalPlayers.SectionVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task LocalSetup_SendsTheJoinCodeThroughUseLocalPlayers()
    {
        // Arrange
        _info = Info(localEnabled: true, joinCodeRequired: true);
        SessionViewModel session = await CreateAvailableSessionAsync();
        _local.EnqueueResults(LocalSessionStatus.Created);
        session.LocalSetup.Reset(session.LocalJoinCodeRequired, joinCodePrefill: null);
        session.LocalSetup.JoinCodeText = " JOINCODE1 ";

        // Act
        bool created = await session.LocalSetup.ConnectAsync();

        // Assert
        created.Should().BeTrue();
        _local.CreatedWithJoinCodes.Should().Equal("JOINCODE1");
    }

    private SessionViewModel CreateSession() =>
        new(
            _client.Object,
            new Mock<IDisplayManagementApi>().Object,
            spotifySetupApi: _setupApi.Object,
            ownSpotifyApp: new OwnSpotifyAppStore(new InMemorySettingsStore()),
            localPlayback: _local);

    private async Task<SessionViewModel> CreateAvailableSessionAsync()
    {
        SessionViewModel session = CreateSession();
        await session.RefreshLocalAvailabilityAsync();
        return session;
    }

    private static SpotifySetupInfoPayload Info(bool localEnabled, bool joinCodeRequired = false) =>
        new()
        {
            ServerClientIdConfigured = true,
            LocalSessionsEnabled = localEnabled,
            LocalSessionJoinCodeRequired = localEnabled && joinCodeRequired,
        };

    private static AuthorizedSessionPayload Local() =>
        new() { Id = "local-1", DisplayName = MACHINE_NAME, Kind = AuthorizedSessionPayload.KIND_LOCAL };

    private static AuthorizedSessionPayload Spotify() =>
        new() { Id = "user-1", DisplayName = "Alice", Kind = AuthorizedSessionPayload.KIND_SPOTIFY };
}
