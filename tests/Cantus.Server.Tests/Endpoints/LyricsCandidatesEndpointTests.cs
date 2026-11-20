using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class LyricsCandidatesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SESSION_ID = "sess-owner";
    private const string TRACK_ID = "track-123";
    private const string DISPLAY_TOKEN = "display-token-tv";

    private static readonly TrackInfo CurrentTrack = new()
    {
        Id = TRACK_ID,
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Duration = TimeSpan.FromSeconds(295)
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();
    private readonly Mock<IDisplayService> _mockDisplayService = new();
    private readonly Mock<ILyricsFetchProvider> _lrclib = new();
    private readonly Mock<ILyricsFetchProvider> _netease = new();

    public LyricsCandidatesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _lrclib.SetupGet(p => p.ProviderName).Returns("LRCLIB");
        _netease.SetupGet(p => p.ProviderName).Returns("NetEase");

        _mockAuthService
            .Setup(a => a.GetSessionByIdAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = SESSION_ID,
                SpotifyUserId = "sp-1",
                DisplayName = "Owner",
                AccessToken = "token",
                RefreshToken = "refresh"
            });

        _mockDisplayService
            .Setup(d => d.ValidateAsync(DISPLAY_TOKEN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayInfo { Id = "display-1", OwnerSessionId = SESSION_ID, Name = "TV" });

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _mockAuthService.Object);
                services.AddScoped(_ => _mockDisplayService.Object);
                services.AddScoped<IReadOnlyList<ILyricsFetchProvider>>(_ => new[] { _lrclib.Object, _netease.Object });
            });
        });
    }

    private static LyricsCandidate Candidate(string provider, string id, bool isSynced) => new(
        provider,
        id,
        "Miss America",
        "Brandon Flowers",
        "The Desired Effect",
        TimeSpan.FromMilliseconds(295011),
        isSynced,
        false,
        ["I'm writing you this letter", "If you ain't figured out"]);

    private void SetCurrentTrack(string sessionId, TrackInfo track)
    {
        IPlaybackSessionRegistry registry = _factory.Services.GetRequiredService<IPlaybackSessionRegistry>();
        registry.UpdateUserState(sessionId, "Owner", new PlaybackState { CurrentTrack = track }, null, 0);
    }

    private static HttpRequestMessage CandidatesRequest(string trackId, string? sessionId)
    {
        HttpRequestMessage message = new(HttpMethod.Get, $"/api/lyrics/{trackId}/candidates");
        if (sessionId is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        }

        return message;
    }

    [Fact]
    public async Task GetCandidates_WithoutCredential_ReturnsUnauthorizedAndQueriesNoProvider()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(CandidatesRequest(TRACK_ID, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _lrclib.Verify(
            p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCandidates_WithUnknownSession_ReturnsUnauthorized()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack("sess-unknown", CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(CandidatesRequest(TRACK_ID, "sess-unknown"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _lrclib.Verify(
            p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("cookie")]
    public async Task GetCandidates_WithDisplayToken_ReturnsForbiddenAndQueriesNoProvider(string slot)
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(SESSION_ID, CurrentTrack);

        HttpRequestMessage request = new(HttpMethod.Get, $"/api/lyrics/{TRACK_ID}/candidates");
        if (slot == "cookie")
        {
            request.Headers.Add("Cookie", $"cantus_display_token={DISPLAY_TOKEN}");
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", DISPLAY_TOKEN);
        }

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _lrclib.Verify(
            p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _netease.Verify(
            p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCandidates_WithSessionCookie_ReturnsOk()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(SESSION_ID, CurrentTrack);
        _lrclib
            .Setup(p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Candidate("LRCLIB", "501", true) });
        _netease
            .Setup(p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LyricsCandidate>());

        HttpRequestMessage request = new(HttpMethod.Get, $"/api/lyrics/{TRACK_ID}/candidates");
        request.Headers.Add("Cookie", $"cantus_session_id={SESSION_ID}");
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsCandidatesDto? dto = await response.Content.ReadFromJsonAsync<LyricsCandidatesDto>();
        dto!.Candidates.Select(c => c.ProviderCandidateId).Should().Equal("501");
    }

    [Fact]
    public async Task GetCandidates_ForOwnersCurrentTrack_ReturnsCandidatesFromEveryProvider()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(SESSION_ID, CurrentTrack);

        _lrclib
            .Setup(p => p.GetCandidatesAsync(
                It.Is<TrackInfo>(t => t.Id == TRACK_ID && t.Title == "Miss America"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Candidate("LRCLIB", "501", true), Candidate("LRCLIB", "502", false) });
        _netease
            .Setup(p => p.GetCandidatesAsync(It.Is<TrackInfo>(t => t.Id == TRACK_ID), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Candidate("NetEase", "111", true) });

        HttpResponseMessage response = await client.SendAsync(CandidatesRequest(TRACK_ID, SESSION_ID));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsCandidatesDto? dto = await response.Content.ReadFromJsonAsync<LyricsCandidatesDto>();
        dto.Should().NotBeNull();
        dto!.TrackId.Should().Be(TRACK_ID);
        dto.UnavailableProviders.Should().BeEmpty();
        dto.Candidates.Select(c => $"{c.Provider}:{c.ProviderCandidateId}")
            .Should().Equal("LRCLIB:501", "LRCLIB:502", "NetEase:111");

        LyricsCandidateDto first = dto.Candidates[0];
        first.Title.Should().Be("Miss America");
        first.Artist.Should().Be("Brandon Flowers");
        first.Album.Should().Be("The Desired Effect");
        first.DurationMs.Should().Be(295011);
        first.IsSynced.Should().BeTrue();
        first.IsInstrumental.Should().BeFalse();
        first.Preview.Should().Equal("I'm writing you this letter", "If you ain't figured out");
        dto.Candidates[1].IsSynced.Should().BeFalse();
    }

    [Fact]
    public async Task GetCandidates_WhenAProviderFails_ReturnsPartialListNotAnError()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(SESSION_ID, CurrentTrack);

        _lrclib
            .Setup(p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));
        _netease
            .Setup(p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Candidate("NetEase", "111", true) });

        HttpResponseMessage response = await client.SendAsync(CandidatesRequest(TRACK_ID, SESSION_ID));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsCandidatesDto? dto = await response.Content.ReadFromJsonAsync<LyricsCandidatesDto>();
        dto!.Candidates.Select(c => c.ProviderCandidateId).Should().Equal("111");
        dto.UnavailableProviders.Should().Equal("LRCLIB");
    }

    [Fact]
    public async Task GetCandidates_ForTrackThatIsNotPlaying_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(SESSION_ID, CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(CandidatesRequest("other-track", SESSION_ID));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _lrclib.Verify(
            p => p.GetCandidatesAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCandidates_WhenNothingIsPlaying_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(CandidatesRequest(TRACK_ID, SESSION_ID));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
