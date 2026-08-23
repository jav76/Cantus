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
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        _mockAuthService
            .Setup(a => a.GetAuthorizationUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new Uri("https://accounts.spotify.com/authorize?test=1"));

        var response = await client.GetAsync("/api/auth/spotify/login?json=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        content.Should().ContainKey("authorizationUrl");
        content!["authorizationUrl"].Should().Contain("accounts.spotify.com");
    }

    [Fact]
    public async Task GetSessions_ReturnsListOfAuthorizedSessions()
    {
        var client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.GetAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserSession>
            {
                new()
                {
                    Id = "sess-1",
                    SpotifyUserId = "sp-1",
                    DisplayName = "Test User",
                    Email = "test@example.com",
                    AccessToken = "token",
                    RefreshToken = "refresh"
                }
            });

        var response = await client.GetAsync("/api/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<List<AuthorizedSessionDto>>();
        sessions.Should().NotBeNull();
        sessions!.Should().HaveCount(1);
        sessions![0].DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task RevokeSession_WhenSessionExists_ReturnsOk()
    {
        var client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.RevokeSessionAsync("sess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await client.DeleteAsync("/api/auth/sessions/sess-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

    }
}
