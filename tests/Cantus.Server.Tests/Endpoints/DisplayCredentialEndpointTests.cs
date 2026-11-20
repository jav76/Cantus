using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

/// <summary>
/// Owner-only endpoints accept a Session and reject a Display token with 403, without
/// side effects. Uses the real auth and Display services over one in-memory database.
/// </summary>
public sealed class DisplayCredentialEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string SESSION_COOKIE = "cantus_session_id";
    private const string DISPLAY_COOKIE = "cantus_display_token";
    private const int TRACK_OFFSET_MS = 250;

    private const string GET_ME = "GET /api/auth/me";
    private const string GET_SESSIONS = "GET /api/auth/sessions";
    private const string DELETE_SESSION = "DELETE /api/auth/sessions/{userId}";
    private const string POST_LOGOUT = "POST /api/auth/logout";
    private const string POST_OFFSET = "POST /api/lyrics/offset";

    private readonly InMemorySessionStore _store = new();
    private readonly Mock<ILyricsCacheRepository> _mockCacheRepo = new();
    private readonly WebApplicationFactory<Program> _factory;

    public DisplayCredentialEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISpotifyAuthService>(_ => _store.CreateAuthService());
                services.AddScoped<IDisplayService>(_ => _store.CreateDisplayService());
                services.AddScoped(_ => _mockCacheRepo.Object);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _store.Dispose();
    }

    public static TheoryData<string, CredentialKind> OwnerEndpointCases()
    {
        string[] endpoints = { GET_ME, GET_SESSIONS, DELETE_SESSION, POST_LOGOUT, POST_OFFSET };

        TheoryData<string, CredentialKind> cases = new();
        foreach (string endpoint in endpoints)
        {
            cases.Add(endpoint, CredentialKind.Session);
            cases.Add(endpoint, CredentialKind.Display);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(OwnerEndpointCases))]
    public async Task OwnerEndpoint_AcceptsSessionAndRejectsDisplayToken(string endpoint, CredentialKind kind)
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        string cookie = kind == CredentialKind.Session
            ? $"{SESSION_COOKIE}={owner.Id}"
            : $"{DISPLAY_COOKIE}={display.Token}";
        string trackId = $"track-{owner.Id}";
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(CreateRequest(endpoint, owner.Id, trackId, cookie));

        if (kind == CredentialKind.Display)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await _store.SessionExistsAsync(owner.Id)).Should().BeTrue();
            _mockCacheRepo.Verify(
                c => c.SetTrackOffsetAsync(trackId, It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertSessionEffectAsync(endpoint, response, owner, trackId);
    }

    [Theory]
    [InlineData("access_token")]
    [InlineData("display_token")]
    public async Task GetCurrentUser_WithDisplayTokenInQuery_ReturnsForbidden(string parameter)
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/auth/me?{parameter}={Uri.EscapeDataString(display.Token)}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCurrentUser_WithRevokedDisplayToken_ReturnsUnauthorized()
    {
        UserSessionEntity owner = await _store.SeedSessionAsync("Owner");
        CreatedDisplay display = await _store.CreateDisplayService().CreateAsync(owner.Id, "TV");
        await _store.CreateDisplayService().RevokeAsync(owner.Id, display.Display.Id);
        HttpClient client = _factory.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", $"{DISPLAY_COOKIE}={display.Token}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task AssertSessionEffectAsync(
        string endpoint,
        HttpResponseMessage response,
        UserSessionEntity owner,
        string trackId)
    {
        switch (endpoint)
        {
            case GET_ME:
                AuthorizedSessionDto? me = await response.Content.ReadFromJsonAsync<AuthorizedSessionDto>();
                me!.Id.Should().Be(owner.Id);
                break;
            case GET_SESSIONS:
                List<AuthorizedSessionDto>? sessions =
                    await response.Content.ReadFromJsonAsync<List<AuthorizedSessionDto>>();
                sessions.Should().ContainSingle().Which.Id.Should().Be(owner.Id);
                break;
            case DELETE_SESSION:
            case POST_LOGOUT:
                (await _store.SessionExistsAsync(owner.Id)).Should().BeFalse();
                break;
            case POST_OFFSET:
                _mockCacheRepo.Verify(
                    c => c.SetTrackOffsetAsync(trackId, TRACK_OFFSET_MS, It.IsAny<CancellationToken>()),
                    Times.Once);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.");
        }
    }

    private static HttpRequestMessage CreateRequest(string endpoint, string ownerId, string trackId, string cookie)
    {
        HttpRequestMessage request = endpoint switch
        {
            GET_ME => new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"),
            GET_SESSIONS => new HttpRequestMessage(HttpMethod.Get, "/api/auth/sessions"),
            DELETE_SESSION => new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{ownerId}"),
            POST_LOGOUT => new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"),
            POST_OFFSET => new HttpRequestMessage(HttpMethod.Post, "/api/lyrics/offset")
            {
                Content = JsonContent.Create(new TrackOffsetDto
                {
                    TrackId = trackId,
                    OffsetMs = TRACK_OFFSET_MS,
                }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.")
        };

        request.Headers.Add("Cookie", cookie);
        return request;
    }
}
