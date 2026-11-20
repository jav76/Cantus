using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class LyricsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SESSION_ID = "sess-1";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ILyricsCacheRepository> _mockCacheRepo = new();
    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();

    public LyricsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _mockCacheRepo.Object);
                services.AddScoped(_ => _mockAuthService.Object);
            });
        });
    }

    [Fact]
    public async Task GetCachedLyrics_WhenFound_ReturnsLyricsDto()
    {
        HttpClient client = _factory.CreateClient();

        SyncedLyrics lyrics = new()
        {
            TrackId = "track-123",
            Title = "Song Title",
            Artist = "Artist Name",
            Lines = [new LyricLine(TimeSpan.FromSeconds(10), "Line 1")]
        };

        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("track-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(lyrics);

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/track-123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsDto? dto = await response.Content.ReadFromJsonAsync<LyricsDto>();
        dto.Should().NotBeNull();
        dto!.TrackId.Should().Be("track-123");
        dto.Lines.Should().HaveCount(1);
        dto.Lines[0].Text.Should().Be("Line 1");
    }

    [Fact]
    public async Task GetCachedLyrics_WhenNotFound_Returns404()
    {
        HttpClient client = _factory.CreateClient();

        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncedLyrics?)null);

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetTrackOffset_WhenAuthenticated_SavesOffsetAndReturnsOk()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.GetSessionByIdAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = SESSION_ID,
                SpotifyUserId = "spotify-1",
                DisplayName = "Owner",
                AccessToken = "access",
                RefreshToken = "refresh",
            });

        TrackOffsetDto request = new()
        {
            TrackId = "track-123",
            OffsetMs = 500
        };

        HttpRequestMessage message = new(HttpMethod.Post, "/api/lyrics/offset")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SESSION_ID);

        HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockCacheRepo.Verify(c => c.SetTrackOffsetAsync("track-123", 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetTrackOffset_WhenUnauthenticated_ReturnsUnauthorizedAndDoesNotWrite()
    {
        HttpClient client = _factory.CreateClient();

        TrackOffsetDto request = new()
        {
            TrackId = "track-anon",
            OffsetMs = 500
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/lyrics/offset", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _mockCacheRepo.Verify(
            c => c.SetTrackOffsetAsync("track-anon", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetTrackOffset_WithUnknownCredential_ReturnsUnauthorizedAndDoesNotWrite()
    {
        HttpClient client = _factory.CreateClient();

        _mockAuthService
            .Setup(a => a.GetSessionByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSession?)null);

        TrackOffsetDto request = new()
        {
            TrackId = "track-unknown",
            OffsetMs = 500
        };

        HttpRequestMessage message = new(HttpMethod.Post, "/api/lyrics/offset")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-session");

        HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _mockCacheRepo.Verify(
            c => c.SetTrackOffsetAsync("track-unknown", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
