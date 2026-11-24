using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class SpotifySetupViewModelTests
{
    private const string OWN_CLIENT_ID = "fedcba9876543210fedcba9876543210";

    [Fact]
    public async Task LoadAsync_WithoutSetupInfo_SignsInThroughTheServerAppWithoutTheScreen()
    {
        (SpotifySetupViewModel setup, _) = Create(info: null);

        await setup.LoadAsync();

        setup.NeedsDialog.Should().BeFalse();
        setup.PrepareLoginPath().Should().Be("login:server");
    }

    [Fact]
    public async Task LoadAsync_WhenOwnAppsAreOff_SkipsTheScreen()
    {
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: true, ownAppsAllowed: false));

        await setup.LoadAsync();

        setup.NeedsDialog.Should().BeFalse();
        setup.PrepareLoginPath().Should().Be("login:server");
    }

    [Fact]
    public async Task LoadAsync_WithServerAppAndNothingSaved_PreselectsTheServerApp()
    {
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: true, ownAppsAllowed: true));

        await setup.LoadAsync();

        setup.NeedsDialog.Should().BeTrue();
        setup.UseOwnApp.Should().BeFalse();
        setup.AppChoiceVisibility.Should().Be(Visibility.Visible);
        setup.OwnAppVisibility.Should().Be(Visibility.Collapsed);
        setup.CanConnect.Should().BeTrue();
        setup.PrepareLoginPath().Should().Be("login:server");
    }

    [Fact]
    public async Task LoadAsync_WithoutServerApp_RequiresTheOwnApp()
    {
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: false, ownAppsAllowed: true));

        await setup.LoadAsync();

        setup.UseOwnApp.Should().BeTrue();
        setup.AppChoiceVisibility.Should().Be(Visibility.Collapsed);
        setup.CanConnect.Should().BeFalse();
        setup.PrepareLoginPath().Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_WithASavedClientId_PreselectsAndFillsTheOwnApp()
    {
        InMemorySettingsStore store = new();
        new OwnSpotifyAppStore(store).Save(OWN_CLIENT_ID);
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: true, ownAppsAllowed: true), store);

        await setup.LoadAsync();

        setup.UseOwnApp.Should().BeTrue();
        setup.ClientIdText.Should().Be(OWN_CLIENT_ID);
        setup.ForgetVisibility.Should().Be(Visibility.Visible);
        setup.PrepareLoginPath().Should().Be($"login:{OWN_CLIENT_ID}");
    }

    [Theory]
    [InlineData("", false, "")]
    [InlineData("abc", false, "A Client ID is 32 letters and digits, from your app's Settings page.")]
    [InlineData("  FEDCBA9876543210FEDCBA9876543210 ", true, "")]
    public async Task ClientIdText_ValidatesAsTheUserTypes(string typed, bool canConnect, string error)
    {
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: false, ownAppsAllowed: true));
        await setup.LoadAsync();

        setup.ClientIdText = typed;

        setup.CanConnect.Should().Be(canConnect);
        setup.ClientIdError.Should().Be(error);
        setup.ClientIdErrorVisibility.Should().Be(error.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
    }

    [Fact]
    public async Task PrepareLoginPath_WithOwnApp_SavesTheNormalizedClientId()
    {
        InMemorySettingsStore store = new();
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: false, ownAppsAllowed: true), store);
        await setup.LoadAsync();
        setup.ClientIdText = OWN_CLIENT_ID.ToUpperInvariant();

        string? path = setup.PrepareLoginPath();

        path.Should().Be($"login:{OWN_CLIENT_ID}");
        new OwnSpotifyAppStore(store).ClientId.Should().Be(OWN_CLIENT_ID);
    }

    [Fact]
    public async Task PrepareLoginPath_SwitchingToTheServerApp_KeepsTheSavedClientId()
    {
        InMemorySettingsStore store = new();
        new OwnSpotifyAppStore(store).Save(OWN_CLIENT_ID);
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: true, ownAppsAllowed: true), store);
        await setup.LoadAsync();

        setup.UseServerApp = true;

        setup.PrepareLoginPath().Should().Be("login:server");
        new OwnSpotifyAppStore(store).ClientId.Should().Be(OWN_CLIENT_ID);
    }

    [Fact]
    public async Task ForgetSavedClientId_ClearsTheStoreAndTheBox()
    {
        InMemorySettingsStore store = new();
        new OwnSpotifyAppStore(store).Save(OWN_CLIENT_ID);
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: false, ownAppsAllowed: true), store);
        await setup.LoadAsync();

        setup.ForgetSavedClientId();

        setup.ClientIdText.Should().BeEmpty();
        setup.HasSavedClientId.Should().BeFalse();
        new OwnSpotifyAppStore(store).ClientId.Should().BeNull();
    }

    [Theory]
    [InlineData(false, Visibility.Visible)]
    [InlineData(true, Visibility.Collapsed)]
    public async Task RedirectUriWarning_FollowsWhetherSpotifyAcceptsTheUri(bool accepted, Visibility expected)
    {
        SpotifySetupInfoPayload info = Info(serverApp: false, ownAppsAllowed: true);
        info.RedirectUriAccepted = accepted;
        (SpotifySetupViewModel setup, _) = Create(info);

        await setup.LoadAsync();

        setup.RedirectUriWarningVisibility.Should().Be(expected);
        setup.RedirectUri.Should().Be(info.RedirectUri);
    }

    [Fact]
    public async Task UseOwnApp_RaisesTheDependentProperties()
    {
        (SpotifySetupViewModel setup, _) = Create(Info(serverApp: true, ownAppsAllowed: true));
        await setup.LoadAsync();
        List<string?> raised = new();
        setup.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        setup.UseOwnApp = true;

        raised.Should().Contain(new[] { nameof(SpotifySetupViewModel.UseServerApp), nameof(SpotifySetupViewModel.OwnAppVisibility), nameof(SpotifySetupViewModel.CanConnect) });
    }

    [Fact]
    public async Task RefreshInfoAsync_TakesTheNewInfoAndKeepsTheChoicesOnTheScreen()
    {
        // Arrange
        (SpotifySetupViewModel setup, Mock<ISpotifySetupApi> api) = Create(Info(serverApp: true, ownAppsAllowed: true));
        await setup.LoadAsync();
        setup.UseOwnApp = true;
        setup.ClientIdText = "typed so far";
        SpotifySetupInfoPayload refreshed = Info(serverApp: true, ownAppsAllowed: true);
        refreshed.LocalSessionsEnabled = true;
        refreshed.LocalSessionJoinCodeRequired = true;
        api.Setup(a => a.GetSetupInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(refreshed);

        // Act
        await setup.RefreshInfoAsync();

        // Assert
        setup.Info!.LocalSessionsEnabled.Should().BeTrue();
        setup.Info.LocalSessionJoinCodeRequired.Should().BeTrue();
        setup.UseOwnApp.Should().BeTrue();
        setup.ClientIdText.Should().Be("typed so far");
    }

    [Theory]
    [InlineData("?connect=spotify", true)]
    [InlineData("connect=Spotify", true)]
    [InlineData("?pair=ABC&connect=spotify", true)]
    [InlineData("?connect=other", false)]
    [InlineData("?reconnect=spotify", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsConnectLink_MatchesOnlyTheConnectSpotifyParameter(string? search, bool expected)
    {
        SpotifySetupViewModel.IsConnectLink(search).Should().Be(expected);
    }

    private static (SpotifySetupViewModel Setup, Mock<ISpotifySetupApi> Api) Create(
        SpotifySetupInfoPayload? info,
        InMemorySettingsStore? store = null)
    {
        Mock<ISpotifySetupApi> api = new();
        api.Setup(a => a.GetSetupInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(info);
        SpotifySetupViewModel setup = new(
            api.Object,
            new OwnSpotifyAppStore(store ?? new InMemorySettingsStore()),
            ownClientId => $"login:{ownClientId ?? "server"}");
        return (setup, api);
    }

    private static SpotifySetupInfoPayload Info(bool serverApp, bool ownAppsAllowed) => new()
    {
        ServerClientIdConfigured = serverApp,
        UserClientIdsAllowed = ownAppsAllowed,
        RedirectUri = "http://127.0.0.1:5000/api/auth/spotify/callback",
        RedirectUriAccepted = true,
        DashboardUrl = "https://developer.spotify.com/dashboard/create",
    };
}
