using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Core.Models;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class SessionViewModelTests
{
    private const string NO_USER_NAME = "None";
    private const string CONNECTED_GLYPH = "";
    private const string CONNECT_GLYPH = "";

    [Fact]
    public void NewSession_IsSignedOut()
    {
        // Arrange & Act
        (_, SessionViewModel session) = CreateSession();

        // Assert
        session.Sessions.Should().BeEmpty();
        session.CurrentUserSession.Should().BeNull();
        session.AuthorizedSessionsCount.Should().Be(0);
        session.ActiveUserId.Should().BeNull();
        session.ActiveUserName.Should().Be(NO_USER_NAME);
        session.IsAuthorized.Should().BeFalse();
        session.ConnectButtonText.Should().Be("Connect Spotify");
        session.ConnectButtonGlyph.Should().Be(CONNECT_GLYPH);
        session.NoSessionsVisibility.Should().Be(Visibility.Visible);
        session.HasSessionsVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ApplySessions_TakesTheFirstSessionAsActiveAndSubscribesToIt()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();

        // Act
        session.ApplySessions(new List<AuthorizedSessionPayload>
        {
            new() { Id = "user-1", DisplayName = "Alice" },
            new() { Id = "user-2", DisplayName = "Bob" },
        });

        // Assert
        session.Sessions.Should().HaveCount(2);
        session.CurrentUserSession!.Id.Should().Be("user-1");
        session.AuthorizedSessionsCount.Should().Be(2);
        session.ActiveUserId.Should().Be("user-1");
        session.ActiveUserName.Should().Be("Alice");
        session.IsAuthorized.Should().BeTrue();
        session.ConnectButtonText.Should().Be("Connected: Alice");
        session.ConnectButtonGlyph.Should().Be(CONNECTED_GLYPH);
        session.NoSessionsVisibility.Should().Be(Visibility.Collapsed);
        session.HasSessionsVisibility.Should().Be(Visibility.Visible);
        client.Verify(c => c.SubscribeToUserAsync("user-1"), Times.Once);
    }

    [Fact]
    public void ApplySessions_WhenEmpty_ClearsAuthorizationAndRaisesSessionEnded()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });
        int endedCount = 0;
        session.SessionEnded += () => endedCount++;

        // Act
        session.ApplySessions(new List<AuthorizedSessionPayload>());

        // Assert
        AssertSignedOut(session);
        endedCount.Should().Be(1);
    }

    [Fact]
    public void ApplySessions_WhenNull_ClearsAuthorizationAndRaisesSessionEnded()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });
        int endedCount = 0;
        session.SessionEnded += () => endedCount++;

        // Act
        session.ApplySessions(null);

        // Assert
        AssertSignedOut(session);
        endedCount.Should().Be(1);
    }

    [Fact]
    public void ApplyAuthSession_ReplacesTheListWithTheNewSessionAndSubscribes()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();
        session.ApplySessions(new List<AuthorizedSessionPayload>
        {
            new() { Id = "user-1", DisplayName = "Alice" },
            new() { Id = "user-2", DisplayName = "Bob" },
        });

        // Act
        session.ApplyAuthSession(new AuthorizedSessionPayload
        {
            Id = "user-new",
            DisplayName = "Carol",
            SpotifyUserId = "sp-carol",
        });

        // Assert
        session.Sessions.Should().ContainSingle().Which.Id.Should().Be("user-new");
        session.AuthorizedSessionsCount.Should().Be(1);
        session.ActiveUserId.Should().Be("user-new");
        session.ActiveUserName.Should().Be("Carol");
        session.IsAuthorized.Should().BeTrue();
        client.Verify(c => c.SubscribeToUserAsync("user-new"), Times.Once);
    }

    [Fact]
    public void ApplyAuthSession_WhenNull_ChangesNothing()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();

        // Act
        session.ApplyAuthSession(null);

        // Assert
        AssertSignedOut(session);
        client.Verify(c => c.SubscribeToUserAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ApplySessionRevoked_ForTheActiveUser_ClearsAndRaisesSessionEnded()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-revoked", DisplayName = "Charlie" });
        int endedCount = 0;
        session.SessionEnded += () => endedCount++;

        // Act
        session.ApplySessionRevoked("user-revoked");

        // Assert
        AssertSignedOut(session);
        endedCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ApplySessionRevoked_WithoutAUserId_ClearsAndRaisesSessionEnded(string? userId)
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });
        int endedCount = 0;
        session.SessionEnded += () => endedCount++;

        // Act
        session.ApplySessionRevoked(userId);

        // Assert
        AssertSignedOut(session);
        endedCount.Should().Be(1);
    }

    [Fact]
    public void ApplySessionRevoked_ForAnotherUser_KeepsTheActiveSession()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });
        int endedCount = 0;
        session.SessionEnded += () => endedCount++;

        // Act
        session.ApplySessionRevoked("someone-else");

        // Assert
        session.Sessions.Should().HaveCount(1);
        session.ActiveUserId.Should().Be("user-1");
        session.IsAuthorized.Should().BeTrue();
        endedCount.Should().Be(0);
    }

    [Fact]
    public async Task LogoutAsync_LogsOutOnTheClientClearsAndRaisesLoggedOut()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Test User" });
        int loggedOutCount = 0;
        int endedCount = 0;
        session.LoggedOut += () => loggedOutCount++;
        session.SessionEnded += () => endedCount++;

        // Act
        await session.LogoutAsync();

        // Assert
        client.Verify(c => c.LogoutAsync(), Times.Once);
        AssertSignedOut(session);
        loggedOutCount.Should().Be(1);
        endedCount.Should().Be(0);
    }

    [Fact]
    public async Task SubscribeToUserAsync_ForwardsToTheClient()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();

        // Act
        await session.SubscribeToUserAsync("user-9");

        // Assert
        client.Verify(c => c.SubscribeToUserAsync("user-9"), Times.Once);
    }

    [Fact]
    public void SpotifyLoginPath_CarriesTheClientId()
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();
        client.SetupGet(c => c.ClientId).Returns("client-7");

        // Act & Assert
        session.SpotifyLoginPath.Should().Be("/api/auth/spotify/login?client_id=client-7");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SpotifyLoginPath_WithoutAClientId_HasNoQuery(string clientId)
    {
        // Arrange
        (Mock<ISignalRPlaybackClient> client, SessionViewModel session) = CreateSession();
        client.SetupGet(c => c.ClientId).Returns(clientId);

        // Act & Assert
        session.SpotifyLoginPath.Should().Be("/api/auth/spotify/login");
    }

    [Fact]
    public void ServerBaseUrl_DerivesBaseUrlFromClient()
    {
        // Arrange
        SessionViewModel session = new(new SignalRPlaybackClient("http://192.168.1.50:5000/hubs/playback"));

        // Act & Assert
        session.ServerBaseUrl.Should().Be("http://192.168.1.50:5000");
    }

    [Fact]
    public void ApplyActiveUser_KeepsTheKnownNameWhenThePollCarriesNone()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        session.ApplyActiveUser("Dana", "user-dana");

        // Act
        session.ApplyActiveUser(null, "user-other");

        // Assert
        session.ActiveUserName.Should().Be("Dana");
        session.ActiveUserId.Should().Be("user-other");
        session.IsAuthorized.Should().BeTrue();
        session.ConnectButtonText.Should().Be("Connected: Dana");
    }

    [Fact]
    public void ApplyActiveUser_NotifiesAuthorizationProperties()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        List<string?> raised = new();
        session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        session.ApplyActiveUser("Dana", "user-dana");

        // Assert
        raised.Should().Contain(new[]
        {
            nameof(SessionViewModel.ActiveUserName),
            nameof(SessionViewModel.ActiveUserId),
            nameof(SessionViewModel.IsAuthorized),
            nameof(SessionViewModel.ConnectButtonText),
            nameof(SessionViewModel.ConnectButtonGlyph),
        });
    }

    [Fact]
    public void ApplyDiagnostics_TakesTheServerSessionCountAndActiveUser()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();

        // Act
        session.ApplyDiagnostics(new DiagnosticsPayload
        {
            AuthorizedSessions = 3,
            ActiveUserName = "Erin",
            ActiveUserId = "user-erin",
        });

        // Assert
        session.AuthorizedSessionsCount.Should().Be(3);
        session.ActiveUserName.Should().Be("Erin");
        session.ActiveUserId.Should().Be("user-erin");
        session.IsAuthorized.Should().BeTrue();
        session.HasSessionsVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void AuthorizedSessionsCount_NotifiesEveryDependentProperty()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        List<string?> raised = new();
        session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        session.AuthorizedSessionsCount = 1;

        // Assert
        raised.Should().BeEquivalentTo(new[]
        {
            nameof(SessionViewModel.AuthorizedSessionsCount),
            nameof(SessionViewModel.NoSessionsVisibility),
            nameof(SessionViewModel.HasSessionsVisibility),
            nameof(SessionViewModel.IsAuthorized),
            nameof(SessionViewModel.ConnectButtonText),
            nameof(SessionViewModel.ConnectButtonGlyph),
        });
        session.ConnectButtonText.Should().Be("Connected");
    }

    [Fact]
    public void ApplySessions_NotifiesTheSessionListProperties()
    {
        // Arrange
        (_, SessionViewModel session) = CreateSession();
        List<string?> raised = new();
        session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        session.ApplySessions(new List<AuthorizedSessionPayload>
        {
            new() { Id = "user-1", DisplayName = "Alice" },
        });

        // Assert
        raised.Should().Contain(new[]
        {
            nameof(SessionViewModel.CurrentUserSession),
            nameof(SessionViewModel.NoSessionsVisibility),
            nameof(SessionViewModel.HasSessionsVisibility),
            nameof(SessionViewModel.IsAuthorized),
            nameof(SessionViewModel.ConnectButtonText),
            nameof(SessionViewModel.ConnectButtonGlyph),
        });
    }

    [Fact]
    public void ApplySessions_StartsDisplayManagementAndLoadsTheDisplays()
    {
        // Arrange
        Mock<IDisplayManagementApi> api = CreateDisplaysApi(new DisplayInfoPayload { Id = "display-1", Name = "TV" });
        (_, SessionViewModel session) = CreateSession(api);

        // Act
        session.ApplySessions(new List<AuthorizedSessionPayload>
        {
            new() { Id = "user-1", DisplayName = "Alice" },
        });

        // Assert
        session.Displays.IsSignedIn.Should().BeTrue();
        session.Displays.SectionVisibility.Should().Be(Visibility.Visible);
        session.Displays.Displays.Should().ContainSingle(d => d.Name == "TV");
        api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ApplyAuthSession_LoadsTheDisplays()
    {
        // Arrange
        Mock<IDisplayManagementApi> api = CreateDisplaysApi(new DisplayInfoPayload { Id = "display-1", Name = "TV" });
        (_, SessionViewModel session) = CreateSession(api);

        // Act
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });

        // Assert
        session.Displays.IsSignedIn.Should().BeTrue();
        session.Displays.Displays.Should().ContainSingle();
    }

    [Fact]
    public void NewSession_DoesNotLoadDisplays()
    {
        // Arrange
        Mock<IDisplayManagementApi> api = CreateDisplaysApi();

        // Act
        (_, SessionViewModel session) = CreateSession(api);

        // Assert
        session.Displays.IsSignedIn.Should().BeFalse();
        session.Displays.SectionVisibility.Should().Be(Visibility.Collapsed);
        api.Verify(a => a.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("empty")]
    [InlineData("logout")]
    public async Task EndingTheSession_EmptiesTheDisplays(string ending)
    {
        // Arrange
        Mock<IDisplayManagementApi> api = CreateDisplaysApi(new DisplayInfoPayload { Id = "display-1", Name = "TV" });
        (_, SessionViewModel session) = CreateSession(api);
        session.ApplyAuthSession(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });

        // Act
        switch (ending)
        {
            case "revoke":
                session.ApplySessionRevoked("user-1");
                break;
            case "empty":
                session.ApplySessions(new List<AuthorizedSessionPayload>());
                break;
            default:
                await session.LogoutAsync();
                break;
        }

        // Assert
        session.Displays.IsSignedIn.Should().BeFalse();
        session.Displays.Displays.Should().BeEmpty();
    }

    private static void AssertSignedOut(SessionViewModel session)
    {
        session.Sessions.Should().BeEmpty();
        session.CurrentUserSession.Should().BeNull();
        session.AuthorizedSessionsCount.Should().Be(0);
        session.ActiveUserId.Should().BeNull();
        session.ActiveUserName.Should().Be(NO_USER_NAME);
        session.IsAuthorized.Should().BeFalse();
    }

    private static (Mock<ISignalRPlaybackClient> Client, SessionViewModel Session) CreateSession(
        Mock<IDisplayManagementApi>? displaysApi = null)
    {
        Mock<ISignalRPlaybackClient> client = new();
        client.Setup(c => c.SubscribeToUserAsync(It.IsAny<string?>())).Returns(Task.CompletedTask);
        client.Setup(c => c.LogoutAsync()).Returns(Task.CompletedTask);
        return (client, new SessionViewModel(client.Object, (displaysApi ?? CreateDisplaysApi()).Object));
    }

    private static Mock<IDisplayManagementApi> CreateDisplaysApi(params DisplayInfoPayload[] displays)
    {
        Mock<IDisplayManagementApi> api = new();
        api
            .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayListResult(DisplayManagementStatus.Succeeded, displays.ToList()));
        return api;
    }
}
