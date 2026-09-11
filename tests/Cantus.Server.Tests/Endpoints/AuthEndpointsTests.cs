using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _mockAuthService.Object);
            });
        });
    }

    [Fact]
    public async Task SpotifyLogin_ReturnsRedirectOrJsonWithAuthUrl()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        _mockAuthService
            .Setup(a => a.GetAuthorizationUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new Uri("https://accounts.spotify.com/authorize?test=1"));

        HttpResponseMessage response = await client.GetAsync("/api/auth/spotify/login?json=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Dictionary<string, string>? content = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        content.Should().ContainKey("authorizationUrl");
        content!["authorizationUrl"].Should().Contain("accounts.spotify.com");
    }

    [Fact]
    public async Task AuthLoginAlias_ReturnsRedirectOrJsonWithAuthUrl()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        _mockAuthService
            .Setup(a => a.GetAuthorizationUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new Uri("https://accounts.spotify.com/authorize?test=alias"));

        HttpResponseMessage response = await client.GetAsync("/api/auth/login?json=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Dictionary<string, string>? content = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        content.Should().ContainKey("authorizationUrl");
        content!["authorizationUrl"].Should().Contain("accounts.spotify.com");
    }

    [Fact]
    public async Task GetSessions_WhenAuthenticated_ReturnsCurrentSession()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.GetSessionAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = "sess-1",
                SpotifyUserId = "sp-1",
                DisplayName = "Test User",
                Email = "test@example.com",
                AccessToken = "token",
                RefreshToken = "refresh"
            });

        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/sessions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sess-1");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<AuthorizedSessionDto>? sessions = await response.Content.ReadFromJsonAsync<List<AuthorizedSessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().HaveCount(1);
        sessions![0].DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task GetSessions_WhenUnauthenticated_ReturnsEmptyList()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<AuthorizedSessionDto>? sessions = await response.Content.ReadFromJsonAsync<List<AuthorizedSessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticatedViaHeader_ReturnsSession()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.GetSessionAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = "sess-1",
                SpotifyUserId = "sp-1",
                DisplayName = "Test User",
                AccessToken = "token",
                RefreshToken = "refresh"
            });

        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sess-1");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AuthorizedSessionDto? session = await response.Content.ReadFromJsonAsync<AuthorizedSessionDto>();
        session.Should().NotBeNull();
        session!.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task Logout_ClearsSessionCookie()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WhenSessionProvided_RevokesSessionAndReturnsOk()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.RevokeSessionAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sess-1");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockAuthService.Verify(a => a.RevokeSessionAsync("sess-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SpotifyCallback_WhenRelinkingMidPlayback_PreservesPlaybackSnapshot()
    {
        // Arrange - the user is mid-song: the registry holds playback, lyrics, and an offset
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        IPlaybackSessionRegistry registry = _factory.Services.GetRequiredService<IPlaybackSessionRegistry>();

        PlaybackState playbackState = new()
        {
            CurrentTrack = new TrackInfo { Id = "track-1", Title = "Song", Artist = "Artist" },
            Progress = TimeSpan.FromSeconds(42),
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        };
        SyncedLyrics lyrics = new()
        {
            TrackId = "track-1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLine> { new(TimeSpan.FromSeconds(1), "La la la") }
        };
        registry.UpdateUserState("sess-relink", "Test User", playbackState, lyrics, 250);

        _mockAuthService
            .Setup(a => a.ExchangeCodeAsync("auth-code", "verifier-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = "sess-relink",
                SpotifyUserId = "sp-relink",
                DisplayName = "Test User",
                AccessToken = "token",
                RefreshToken = "refresh"
            });

        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/spotify/callback?code=auth-code&state=state-1");
        request.Headers.Add("Cookie", "cantus_oauth_state=state-1; cantus_pkce_verifier=verifier-1");

        // Act - the user re-links their Spotify account while the song is playing
        HttpResponseMessage response = await client.SendAsync(request);

        // Assert - the callback must not wipe the in-memory playback snapshot
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        UserPlaybackSnapshot? snapshot = registry.GetUserState("sess-relink");
        snapshot.Should().NotBeNull();
        snapshot!.PlaybackState.Should().NotBeNull("re-linking must not discard the current playback state");
        snapshot.PlaybackState!.CurrentTrack!.Id.Should().Be("track-1");
        snapshot.PlaybackState.IsPlaying.Should().BeTrue();
        snapshot.Lyrics.Should().NotBeNull("re-linking must not discard the current lyrics");
        snapshot.Lyrics!.Lines.Should().NotBeEmpty();
        snapshot.TrackOffsetMs.Should().Be(250, "re-linking must not reset the user's saved track offset");
        snapshot.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task RevokeSession_WhenCallerOwnsSession_ReturnsOk()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.RevokeSessionAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        HttpRequestMessage message = new(HttpMethod.Delete, "/api/auth/sessions/sess-1");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sess-1");

        HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeSession_WhenUnauthenticated_ReturnsUnauthorizedAndDoesNotRevoke()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.DeleteAsync("/api/auth/sessions/sess-1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _mockAuthService.Verify(
            a => a.RevokeSessionAsync("sess-1", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeSession_WhenCallerIsADifferentSession_ReturnsForbiddenAndDoesNotRevoke()
    {
        HttpClient client = _factory.CreateClient();

        HttpRequestMessage message = new(HttpMethod.Delete, "/api/auth/sessions/victim-session");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "attacker-session");

        HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _mockAuthService.Verify(
            a => a.RevokeSessionAsync("victim-session", It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
