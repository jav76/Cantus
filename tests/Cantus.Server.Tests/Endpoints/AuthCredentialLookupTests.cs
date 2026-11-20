using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Models;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>
/// Request credentials resolve a session by its exact session id only. These tests use
/// the real auth service so they cover both the database and the in-memory cache.
/// </summary>
public sealed class AuthCredentialLookupTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string SESSION_COOKIE = "cantus_session_id";

    private readonly InMemorySessionStore _store = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AuthCredentialLookupTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISpotifyAuthService>(_ => _store.CreateAuthService());
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task GetCurrentUser_WithSessionIdCookie_ReturnsSession()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(CreateCookieRequest(HttpMethod.Get, "/api/auth/me", seeded.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AuthorizedSessionDto? dto = await response.Content.ReadFromJsonAsync<AuthorizedSessionDto>();
        dto!.Id.Should().Be(seeded.Id);
    }

    [Fact]
    public async Task GetCurrentUser_WithSpotifyUserIdCookie_ReturnsUnauthorized()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            CreateCookieRequest(HttpMethod.Get, "/api/auth/me", seeded.SpotifyUserId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrentUser_WithSpotifyUserIdAfterSessionIsCached_ReturnsUnauthorized()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage warmUp = await client.SendAsync(CreateCookieRequest(HttpMethod.Get, "/api/auth/me", seeded.Id));
        warmUp.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage response = await client.SendAsync(
            CreateCookieRequest(HttpMethod.Get, "/api/auth/me", seeded.SpotifyUserId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("token")]
    public async Task GetCurrentUser_WithSpotifyUserIdInQuery_ReturnsUnauthorized(string queryParameter)
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/auth/me?{queryParameter}={Uri.EscapeDataString(seeded.SpotifyUserId)}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSessions_WithSessionIdCookie_ReturnsOwnSession()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            CreateCookieRequest(HttpMethod.Get, "/api/auth/sessions", seeded.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<AuthorizedSessionDto>? sessions = await response.Content.ReadFromJsonAsync<List<AuthorizedSessionDto>>();
        sessions.Should().ContainSingle().Which.Id.Should().Be(seeded.Id);
    }

    [Fact]
    public async Task GetSessions_WithSpotifyUserIdCookie_ReturnsEmptyList()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            CreateCookieRequest(HttpMethod.Get, "/api/auth/sessions", seeded.SpotifyUserId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<AuthorizedSessionDto>? sessions = await response.Content.ReadFromJsonAsync<List<AuthorizedSessionDto>>();
        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeSession_WithSpotifyUserId_ReturnsNotFoundAndKeepsSession()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(CreateCookieRequest(
            HttpMethod.Delete,
            $"/api/auth/sessions/{seeded.SpotifyUserId}",
            seeded.SpotifyUserId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _store.SessionExistsAsync(seeded.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Logout_WithSpotifyUserIdCookie_KeepsSession()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            CreateCookieRequest(HttpMethod.Post, "/api/auth/logout", seeded.SpotifyUserId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _store.SessionExistsAsync(seeded.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Logout_WithSessionIdCookie_RevokesSession()
    {
        UserSessionEntity seeded = await _store.SeedSessionAsync("Owner");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            CreateCookieRequest(HttpMethod.Post, "/api/auth/logout", seeded.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _store.SessionExistsAsync(seeded.Id)).Should().BeFalse();
    }

    private static HttpRequestMessage CreateCookieRequest(HttpMethod method, string path, string credential)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("Cookie", $"{SESSION_COOKIE}={credential}");
        return request;
    }
}
