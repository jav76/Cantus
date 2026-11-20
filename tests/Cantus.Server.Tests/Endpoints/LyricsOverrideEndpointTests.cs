using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class LyricsOverrideEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OWNER_SESSION_ID = "sess-owner";
    private const string OTHER_SESSION_ID = "sess-other";
    private const string ELSEWHERE_SESSION_ID = "sess-elsewhere";
    private const string TRACK_ID = "track-123";
    private const string DISPLAY_TOKEN = "display-token-tv";
    private const int OWNER_OFFSET_MS = 250;

    private static readonly TrackInfo CurrentTrack = new()
    {
        Id = TRACK_ID,
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Duration = TimeSpan.FromSeconds(295)
    };

    private static readonly TrackInfo OtherTrack = new()
    {
        Id = "track-other",
        Title = "Other Song",
        Artist = "Other Artist"
    };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ISpotifyAuthService> _mockAuthService = new();
    private readonly Mock<IDisplayService> _mockDisplayService = new();
    private readonly Mock<ILyricsCacheRepository> _mockRepository = new();
    private readonly Mock<ILyricsFetchProvider> _lrclib = new();
    private readonly Mock<ILyricsFetchProvider> _netease = new();
    private readonly Mock<IHubContext<PlaybackHub, IPlaybackClient>> _mockHubContext = new();
    private readonly Mock<IHubClients<IPlaybackClient>> _mockHubClients = new();
    private readonly Mock<IPlaybackClient> _ownerGroup = new();
    private readonly Mock<IPlaybackClient> _otherGroup = new();
    private readonly Mock<IPlaybackClient> _elsewhereGroup = new();
    private readonly Mock<IPlaybackClient> _ownerDisplays = new();
    private readonly Mock<IPlaybackClient> _otherDisplays = new();
    private readonly Mock<IPlaybackClient> _elsewhereDisplays = new();

    public LyricsOverrideEndpointTests(WebApplicationFactory<Program> factory)
    {
        _lrclib.SetupGet(p => p.ProviderName).Returns("LRCLIB");
        _netease.SetupGet(p => p.ProviderName).Returns("NetEase");
        _lrclib
            .Setup(p => p.FetchLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LyricsFetchResult.NotFound());
        _netease
            .Setup(p => p.FetchLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LyricsFetchResult.NotFound());

        _mockAuthService
            .Setup(a => a.GetSessionByIdAsync(OWNER_SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = OWNER_SESSION_ID,
                SpotifyUserId = "sp-1",
                DisplayName = "Owner",
                AccessToken = "token",
                RefreshToken = "refresh"
            });

        _mockDisplayService
            .Setup(d => d.ValidateAsync(DISPLAY_TOKEN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayInfo { Id = "display-1", OwnerSessionId = OWNER_SESSION_ID, Name = "TV" });

        _mockHubContext.SetupGet(h => h.Clients).Returns(_mockHubClients.Object);
        _mockHubClients.Setup(c => c.Group($"user_{OWNER_SESSION_ID}")).Returns(_ownerGroup.Object);
        _mockHubClients.Setup(c => c.Group($"user_{OTHER_SESSION_ID}")).Returns(_otherGroup.Object);
        _mockHubClients.Setup(c => c.Group($"user_{ELSEWHERE_SESSION_ID}")).Returns(_elsewhereGroup.Object);
        _mockHubClients.Setup(c => c.Group($"display_{OWNER_SESSION_ID}")).Returns(_ownerDisplays.Object);
        _mockHubClients.Setup(c => c.Group($"display_{OTHER_SESSION_ID}")).Returns(_otherDisplays.Object);
        _mockHubClients.Setup(c => c.Group($"display_{ELSEWHERE_SESSION_ID}")).Returns(_elsewhereDisplays.Object);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _mockAuthService.Object);
                services.AddScoped(_ => _mockDisplayService.Object);
                services.AddScoped(_ => _mockRepository.Object);
                services.AddScoped<IReadOnlyList<ILyricsFetchProvider>>(_ => new[] { _lrclib.Object, _netease.Object });
                services.AddSingleton(_mockHubContext.Object);
            });
        });
    }

    private IPlaybackSessionRegistry Registry => _factory.Services.GetRequiredService<IPlaybackSessionRegistry>();

    private void SetCurrentTrack(string sessionId, TrackInfo track, SyncedLyrics? lyrics = null, int offsetMs = 0)
    {
        Registry.UpdateUserState(sessionId, sessionId, new PlaybackState { CurrentTrack = track }, lyrics, offsetMs);
    }

    private static SyncedLyrics Lyrics(string text) => new()
    {
        TrackId = TRACK_ID,
        Title = CurrentTrack.Title,
        Artist = CurrentTrack.Artist,
        IsSynced = true,
        Lines = [new LyricLine(TimeSpan.FromSeconds(1), text)]
    };

    private static HttpRequestMessage OverrideRequest(
        string trackId,
        string? sessionId,
        LyricsOverrideRequestDto body)
    {
        HttpRequestMessage message = new(HttpMethod.Post, $"/api/lyrics/{trackId}/override")
        {
            Content = JsonContent.Create(body)
        };

        if (sessionId is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        }

        return message;
    }

    private static LyricsOverrideRequestDto UseCandidate(string provider, string candidateId) => new()
    {
        Action = LyricsOverrideRequestDto.ACTION_CANDIDATE,
        Provider = provider,
        CandidateId = candidateId
    };

    private void VerifyNoOverrideWritten()
    {
        _mockRepository.Verify(
            r => r.SaveLyricsOverrideAsync(It.IsAny<LyricsOverride>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            r => r.RemoveLyricsOverrideAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void VerifyNothingBroadcast()
    {
        _ownerGroup.Verify(g => g.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Never);
        _otherGroup.Verify(g => g.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Never);
        _ownerDisplays.Verify(g => g.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Never);
    }

    [Fact]
    public async Task Override_WithoutCredential_ReturnsUnauthorizedAndChangesNothing()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, null, new LyricsOverrideRequestDto { Action = "noLyrics" }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifyNoOverrideWritten();
        VerifyNothingBroadcast();
    }

    [Fact]
    public async Task Override_WithUnknownSession_ReturnsUnauthorized()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack("sess-unknown", CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, "sess-unknown", new LyricsOverrideRequestDto { Action = "noLyrics" }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifyNoOverrideWritten();
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("cookie")]
    public async Task Override_WithDisplayToken_ReturnsForbiddenAndChangesNothing(string slot)
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Wrong match"));

        HttpRequestMessage request = OverrideRequest(TRACK_ID, null, new LyricsOverrideRequestDto { Action = "noLyrics" });
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
        VerifyNoOverrideWritten();
        VerifyNothingBroadcast();
        Registry.GetUserState(OWNER_SESSION_ID)!.Lyrics!.Lines[0].Text.Should().Be("Wrong match");
    }

    [Fact]
    public async Task Override_WithSessionCookie_AppliesTheOverride()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Wrong match"));

        HttpRequestMessage request = OverrideRequest(TRACK_ID, null, new LyricsOverrideRequestDto { Action = "noLyrics" });
        request.Headers.Add("Cookie", $"cantus_session_id={OWNER_SESSION_ID}");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockRepository.Verify(
            r => r.SaveLyricsOverrideAsync(
                It.Is<LyricsOverride>(o => o.TrackId == TRACK_ID && o.Kind == LyricsOverrideKind.NoLyrics),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Override_ForTrackThatIsNotPlaying_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, OtherTrack);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "noLyrics" }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        VerifyNoOverrideWritten();
    }

    [Fact]
    public async Task Override_WithUnknownAction_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "translate" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNoOverrideWritten();
    }

    [Fact]
    public async Task UseCandidate_StoresOverrideAndBroadcastsToEveryoneOnTheTrack()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Wrong match"), OWNER_OFFSET_MS);
        SetCurrentTrack(OTHER_SESSION_ID, CurrentTrack, Lyrics("Wrong match"));
        SetCurrentTrack(ELSEWHERE_SESSION_ID, OtherTrack);

        _netease
            .Setup(p => p.GetCandidateLyricsAsync(
                It.Is<TrackInfo>(t => t.Id == TRACK_ID),
                "111",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Lyrics("Right match"));

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, UseCandidate("NetEase", "111")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsDto? dto = await response.Content.ReadFromJsonAsync<LyricsDto>();
        dto!.Lines.Select(l => l.Text).Should().Equal("Right match");

        _mockRepository.Verify(
            r => r.SaveLyricsOverrideAsync(
                It.Is<LyricsOverride>(o =>
                    o.TrackId == TRACK_ID
                    && o.Kind == LyricsOverrideKind.Candidate
                    && o.Provider == "NetEase"
                    && o.ProviderCandidateId == "111"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _ownerGroup.Verify(
            g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID && l.Lines[0].Text == "Right match")),
            Times.Once);
        _otherGroup.Verify(
            g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.Lines[0].Text == "Right match")),
            Times.Once);
        _elsewhereGroup.Verify(g => g.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Never);
        _ownerDisplays.Verify(
            g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.Lines[0].Text == "Right match")),
            Times.Once);
        _otherDisplays.Verify(
            g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.Lines[0].Text == "Right match")),
            Times.Once);
        _elsewhereDisplays.Verify(g => g.ReceiveLyrics(It.IsAny<LyricsDto>()), Times.Never);

        UserPlaybackSnapshot owner = Registry.GetUserState(OWNER_SESSION_ID)!;
        owner.Lyrics!.Lines[0].Text.Should().Be("Right match");
        owner.TrackOffsetMs.Should().Be(OWNER_OFFSET_MS);
        Registry.GetUserState(OTHER_SESSION_ID)!.Lyrics!.Lines[0].Text.Should().Be("Right match");
    }

    [Fact]
    public async Task UseCandidate_WithoutProviderOrCandidateId_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "candidate" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNoOverrideWritten();
    }

    [Fact]
    public async Task UseCandidate_FromUnknownProvider_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, UseCandidate("Musixmatch", "1")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNoOverrideWritten();
        VerifyNothingBroadcast();
    }

    [Fact]
    public async Task UseCandidate_ThatTheProviderDoesNotHave_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack);

        _lrclib
            .Setup(p => p.GetCandidateLyricsAsync(It.IsAny<TrackInfo>(), "999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncedLyrics?)null);

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, UseCandidate("LRCLIB", "999")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        VerifyNoOverrideWritten();
        VerifyNothingBroadcast();
    }

    [Fact]
    public async Task UseCandidate_WhenTheProviderFails_ReturnsBadGatewayAndKeepsTheCurrentLyrics()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Current"));

        _lrclib
            .Setup(p => p.GetCandidateLyricsAsync(It.IsAny<TrackInfo>(), "501", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, UseCandidate("LRCLIB", "501")));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        VerifyNoOverrideWritten();
        VerifyNothingBroadcast();
        Registry.GetUserState(OWNER_SESSION_ID)!.Lyrics!.Lines[0].Text.Should().Be("Current");
    }

    [Fact]
    public async Task MarkNoLyrics_StoresOverrideAndBroadcastsEmptyLyrics()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Wrong match"));
        SetCurrentTrack(OTHER_SESSION_ID, CurrentTrack, Lyrics("Wrong match"));

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "noLyrics" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockRepository.Verify(
            r => r.SaveLyricsOverrideAsync(
                It.Is<LyricsOverride>(o => o.TrackId == TRACK_ID && o.Kind == LyricsOverrideKind.NoLyrics),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _ownerGroup.Verify(
            g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.TrackId == TRACK_ID && l.Lines.Count == 0 && !l.IsInstrumental)),
            Times.Once);
        _otherGroup.Verify(g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.Lines.Count == 0)), Times.Once);
        Registry.GetUserState(OWNER_SESSION_ID)!.Lyrics.Should().BeNull();
        Registry.GetUserState(OTHER_SESSION_ID)!.Lyrics.Should().BeNull();
    }

    [Fact]
    public async Task Refetch_DropsCachedMatchAndBroadcastsTheNewAutomaticMatch()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Stale"));

        _lrclib
            .Setup(p => p.FetchLyricsAsync(It.Is<TrackInfo>(t => t.Id == TRACK_ID), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LyricsFetchResult.Found(Lyrics("Fresh")));

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "refetch" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockRepository.Verify(r => r.RemoveLyricsOverrideAsync(TRACK_ID, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(
            r => r.SaveLyricsAsync(
                It.Is<SyncedLyrics>(l => l.TrackId == TRACK_ID),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _ownerGroup.Verify(g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.Lines[0].Text == "Fresh")), Times.Once);
        Registry.GetUserState(OWNER_SESSION_ID)!.Lyrics!.Lines[0].Text.Should().Be("Fresh");
    }

    [Fact]
    public async Task Refetch_WhenAProviderIsUnreachable_ReturnsBadGatewayAndKeepsTheCurrentLyrics()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Current"));

        _lrclib
            .Setup(p => p.FetchLyricsAsync(It.Is<TrackInfo>(t => t.Id == TRACK_ID), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LyricsFetchResult.Unavailable());

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "refetch" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        VerifyNoOverrideWritten();
        VerifyNothingBroadcast();
        _mockRepository.Verify(
            r => r.SaveLyricsAsync(It.IsAny<SyncedLyrics>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            r => r.MarkNotFoundAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Registry.GetUserState(OWNER_SESSION_ID)!.Lyrics!.Lines[0].Text.Should().Be("Current");
    }

    [Fact]
    public async Task Clear_ReturnsTheTrackToAutomaticMatchingAndBroadcastsIt()
    {
        HttpClient client = _factory.CreateClient();
        SetCurrentTrack(OWNER_SESSION_ID, CurrentTrack, Lyrics("Override"));

        _mockRepository
            .Setup(r => r.GetCachedLyricsAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Lyrics("Automatic"));

        HttpResponseMessage response = await client.SendAsync(
            OverrideRequest(TRACK_ID, OWNER_SESSION_ID, new LyricsOverrideRequestDto { Action = "clear" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsDto? dto = await response.Content.ReadFromJsonAsync<LyricsDto>();
        dto!.Lines.Select(l => l.Text).Should().Equal("Automatic");
        _mockRepository.Verify(r => r.RemoveLyricsOverrideAsync(TRACK_ID, It.IsAny<CancellationToken>()), Times.Once);
        _lrclib.Verify(
            p => p.FetchLyricsAsync(It.IsAny<TrackInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _ownerGroup.Verify(g => g.ReceiveLyrics(It.Is<LyricsDto>(l => l.Lines[0].Text == "Automatic")), Times.Once);
    }
}
