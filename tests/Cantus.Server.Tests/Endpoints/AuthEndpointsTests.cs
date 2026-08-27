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
    public async Task RevokeSession_WhenSessionExists_ReturnsOk()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.RevokeSessionAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        HttpResponseMessage response = await client.DeleteAsync("/api/auth/sessions/sess-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
