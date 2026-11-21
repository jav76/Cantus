using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task GetCachedLyrics_WithWordTiming_SendsSyllablesToClients()
    {
        HttpClient client = _factory.CreateClient();

        SyncedLyrics lyrics = new()
        {
            TrackId = "track-words",
            Title = "Song Title",
            Artist = "Artist Name",
            IsSynced = true,
            Lines =
            [
                new LyricLine(
                    TimeSpan.FromMilliseconds(27360),
                    "alpha bravo",
                    [
                        new LyricSyllable(TimeSpan.FromMilliseconds(27360), TimeSpan.FromMilliseconds(240), "alpha "),
                        new LyricSyllable(TimeSpan.FromMilliseconds(27600), TimeSpan.FromMilliseconds(95), "bravo")
                    ]),
                new LyricLine(TimeSpan.FromMilliseconds(30000), "line only")
            ]
        };

        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("track-words", It.IsAny<CancellationToken>()))
            .ReturnsAsync(lyrics);

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/track-words");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement lines = document.RootElement.GetProperty("lines");

        JsonElement syllables = lines[0].GetProperty("syllables");
        syllables.GetArrayLength().Should().Be(2);
        syllables[0].GetProperty("timestampMs").GetInt64().Should().Be(27360);
        syllables[0].GetProperty("durationMs").GetInt64().Should().Be(240);
        syllables[0].GetProperty("text").GetString().Should().Be("alpha ");
        syllables[1].GetProperty("timestampMs").GetInt64().Should().Be(27600);
        syllables[1].GetProperty("durationMs").GetInt64().Should().Be(95);
        syllables[1].GetProperty("text").GetString().Should().Be("bravo");

        lines[1].GetProperty("syllables").ValueKind.Should().Be(JsonValueKind.Null);
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
    public async Task GetCachedLyrics_WithCandidateOverride_ReturnsTheOverride()
    {
        HttpClient client = _factory.CreateClient();

        SyncedLyrics chosen = new()
        {
            TrackId = "track-override",
            Title = "Song Title",
            Artist = "Artist Name",
            Lines = [new LyricLine(TimeSpan.FromSeconds(10), "Chosen line")]
        };

        _mockCacheRepo
            .Setup(c => c.GetLyricsOverrideAsync("track-override", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LyricsOverride.ForCandidate("LRCLIB", "501", chosen));
        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("track-override", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncedLyrics
            {
                TrackId = "track-override",
                Title = "Song Title",
                Artist = "Artist Name",
                Lines = [new LyricLine(TimeSpan.FromSeconds(10), "Automatic line")]
            });

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/track-override");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsDto? dto = await response.Content.ReadFromJsonAsync<LyricsDto>();
        dto!.Lines[0].Text.Should().Be("Chosen line");
    }

    [Fact]
    public async Task GetCachedLyrics_WithNoLyricsOverride_Returns404()
    {
        HttpClient client = _factory.CreateClient();

        _mockCacheRepo
            .Setup(c => c.GetLyricsOverrideAsync("track-silenced", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LyricsOverride.ForNoLyrics("track-silenced"));
        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("track-silenced", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncedLyrics { TrackId = "track-silenced", Title = "T", Artist = "A" });

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/track-silenced");

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
