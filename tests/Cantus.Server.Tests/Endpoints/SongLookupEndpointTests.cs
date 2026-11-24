using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Lyrics.Lookup;
using Cantus.Server.BackgroundServices;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class SongLookupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SEARCH_PATH = "/api/lookup/search";
    private const string PLAY_PATH = "/api/lookup/play";
    private const string SESSION_ID = "sess-owner";
    private const string SPOTIFY_USER_ID = "sp-owner";
    private const string ACCESS_TOKEN = "owner-access-token";
    private const string DISPLAY_TOKEN = "display-token-tv";
    private const string TRACK_ID = "4u7EnebtmKWzUH433cf5Qv";
    private const string RESOLVED_TRACK_ID = "7tFiyTwD0nx5a1eklYtX2J";
    private const string FRAGMENT = "is this the real life is this just fantasy";
    private const int RETRY_AFTER_SECONDS = 30;
    private const int SONG_DURATION_MS = 354_000;
    private const int LOWERED_PERMIT_LIMIT = 2;

    private readonly WebApplicationFactory<Program> _baseFactory;
    private readonly Mock<ISpotifyAuthService> _authService = new();
    private readonly Mock<IDisplayService> _displayService = new();
    private readonly Mock<ISongLookupService> _lookupService = new();
    private readonly Mock<ISongPlaybackService> _playbackService = new();
    private readonly Mock<ISpotifyPlayerClient> _playerClient = new();

    public SongLookupEndpointTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;

        _authService
            .Setup(a => a.GetSessionByIdAsync(SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession
            {
                Id = SESSION_ID,
                SpotifyUserId = SPOTIFY_USER_ID,
                DisplayName = "Owner",
                AccessToken = ACCESS_TOKEN,
                RefreshToken = "refresh"
            });

        _displayService
            .Setup(d => d.ValidateAsync(DISPLAY_TOKEN, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayInfo { Id = "display-1", OwnerSessionId = SESSION_ID, Name = "TV" });

        _lookupService
            .Setup(s => s.LookupAsync(
                It.IsAny<string>(),
                It.IsAny<SongLookupScope>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongLookupResult(SongMatchOutcome.None, false, [], []));
    }

    /// <param name="realPlayback">
    /// Uses the real play service over a mocked Spotify player client instead of a mocked play service.
    /// </param>
    private WebApplicationFactory<Program> CreateFactory(
        bool realPlayback = false,
        Action<SongLookupOptions>? configureLookup = null)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ServiceDescriptor? poller = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(ActiveUsersPlaybackMonitor));
                if (poller is not null)
                {
                    services.Remove(poller);
                }

                services.AddScoped(_ => _authService.Object);
                services.AddScoped(_ => _displayService.Object);
                services.AddScoped(_ => _lookupService.Object);
                if (realPlayback)
                {
                    services.AddScoped(_ => _playerClient.Object);
                }
                else
                {
                    services.AddScoped(_ => _playbackService.Object);
                }

                if (configureLookup is not null)
                {
                    services.Configure(configureLookup);
                }
            });
        });
    }

    private static HttpRequestMessage Post(string path, object body, string? sessionId = SESSION_ID)
    {
        HttpRequestMessage message = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        if (sessionId is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        }

        return message;
    }

    private static HttpRequestMessage PostRaw(string path, string json)
    {
        HttpRequestMessage message = new(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SESSION_ID);
        return message;
    }

    private void VerifyNoServiceCalled()
    {
        _lookupService.Verify(
            s => s.LookupAsync(
                It.IsAny<string>(),
                It.IsAny<SongLookupScope>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _playbackService.Verify(
            s => s.PlayAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<SongPlayRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupPlayResult(SongPlayResult result)
    {
        _playbackService
            .Setup(s => s.PlayAsync(
                SESSION_ID,
                ACCESS_TOKEN,
                It.IsAny<SongPlayRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    [Theory]
    [InlineData(SEARCH_PATH)]
    [InlineData(PLAY_PATH)]
    public async Task Post_WithoutCredential_ReturnsUnauthorizedAndCallsNoService(string path)
    {
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            Post(path, new { fragment = FRAGMENT, action = "play", spotifyTrackId = TRACK_ID }, sessionId: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifyNoServiceCalled();
    }

    [Theory]
    [InlineData(SEARCH_PATH)]
    [InlineData(PLAY_PATH)]
    public async Task Post_WithUnknownSession_ReturnsUnauthorized(string path)
    {
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            Post(path, new { fragment = FRAGMENT, action = "play", spotifyTrackId = TRACK_ID }, "sess-unknown"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifyNoServiceCalled();
    }

    [Theory]
    [InlineData(SEARCH_PATH, "Authorization")]
    [InlineData(SEARCH_PATH, "cookie")]
    [InlineData(PLAY_PATH, "Authorization")]
    [InlineData(PLAY_PATH, "cookie")]
    public async Task Post_WithDisplayToken_ReturnsForbiddenAndCallsNoService(string path, string slot)
    {
        HttpClient client = CreateFactory().CreateClient();
        HttpRequestMessage request = Post(
            path,
            new { fragment = FRAGMENT, action = "play", spotifyTrackId = TRACK_ID },
            sessionId: null);
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
        VerifyNoServiceCalled();
    }

    [Theory]
    [InlineData(SEARCH_PATH)]
    [InlineData(PLAY_PATH)]
    public async Task Post_WithLocalSession_ReturnsForbiddenAndCallsNoService(string path)
    {
        const string LOCAL_SESSION_ID = "sess-local";
        _authService
            .Setup(a => a.GetSessionByIdAsync(LOCAL_SESSION_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSession { Id = LOCAL_SESSION_ID, Kind = SessionKind.Local, DisplayName = "Desktop" });
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            Post(path, new { fragment = FRAGMENT, action = "play", spotifyTrackId = TRACK_ID }, LOCAL_SESSION_ID));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        VerifyNoServiceCalled();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"scope\":\"local\"}")]
    [InlineData("{\"fragment\":\"   \"}")]
    [InlineData("{\"fragment\":\"hello there\"}")]
    [InlineData("{\"fragment\":\"is this the real life\",\"scope\":\"everywhere\"}")]
    public async Task Search_WithInvalidBody_ReturnsBadRequestAndDoesNotLookUp(string json)
    {
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(PostRaw(SEARCH_PATH, json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("message");
        VerifyNoServiceCalled();
    }

    [Fact]
    public async Task Search_WithFragmentOverTheCharacterLimit_ReturnsBadRequest()
    {
        HttpClient client = CreateFactory().CreateClient();
        string fragment = new('a', LyricMatchConstants.MAX_FRAGMENT_CHARACTERS + 1);

        HttpResponseMessage response = await client.SendAsync(Post(SEARCH_PATH, new { fragment }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNoServiceCalled();
    }

    [Fact]
    public async Task Search_WhenTheServiceRejectsTheFragment_ReturnsBadRequest()
    {
        _lookupService
            .Setup(s => s.LookupAsync(
                It.IsAny<string>(),
                It.IsAny<SongLookupScope>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("The fragment cannot be searched (TooShort)."));
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(Post(SEARCH_PATH, new { fragment = FRAGMENT }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null, SongLookupScope.Auto)]
    [InlineData("auto", SongLookupScope.Auto)]
    [InlineData("local", SongLookupScope.Local)]
    [InlineData("online", SongLookupScope.Online)]
    public async Task Search_PassesTheScopeAndTheCallersSpotifyUserId(string? scope, SongLookupScope expected)
    {
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(Post(SEARCH_PATH, new { fragment = FRAGMENT, scope }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _lookupService.Verify(
            s => s.LookupAsync(FRAGMENT, expected, SPOTIFY_USER_ID, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Search_WithSessionCookie_ReturnsOk()
    {
        HttpClient client = CreateFactory().CreateClient();
        HttpRequestMessage request = Post(SEARCH_PATH, new { fragment = FRAGMENT }, sessionId: null);
        request.Headers.Add("Cookie", $"cantus_session_id={SESSION_ID}");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_ReturnsTheDocumentedWireShape()
    {
        _lookupService
            .Setup(s => s.LookupAsync(
                It.IsAny<string>(),
                It.IsAny<SongLookupScope>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SongLookupResult(
                SongMatchOutcome.Accepted,
                false,
                [
                    new SongGuess(
                        "Local",
                        TRACK_ID,
                        "Bohemian Rhapsody",
                        "Queen",
                        "A Night at the Opera",
                        null,
                        0.8649,
                        true,
                        ["Is this the real life?", "Is this just fantasy?"],
                        2),
                    new SongGuess(
                        "NetEase",
                        null,
                        "Bohemian Rhapsody",
                        "Queen",
                        null,
                        SONG_DURATION_MS,
                        0.31,
                        false,
                        [],
                        1)
                ],
                [
                    new SongLookupProviderStatus("NetEase", SongLookupProviderState.Ok),
                    new SongLookupProviderStatus(
                        "LRCLIB",
                        SongLookupProviderState.RateLimited,
                        TimeSpan.FromSeconds(RETRY_AFTER_SECONDS - 0.5))
                ]));
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(Post(SEARCH_PATH, new { fragment = FRAGMENT }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        root.GetProperty("outcome").GetString().Should().Be("accepted");
        root.GetProperty("onlineRecommended").GetBoolean().Should().BeFalse();

        JsonElement local = root.GetProperty("guesses")[0];
        local.GetProperty("source").GetString().Should().Be("Local");
        local.GetProperty("spotifyTrackId").GetString().Should().Be(TRACK_ID);
        local.GetProperty("title").GetString().Should().Be("Bohemian Rhapsody");
        local.GetProperty("artist").GetString().Should().Be("Queen");
        local.GetProperty("album").GetString().Should().Be("A Night at the Opera");
        local.GetProperty("durationMs").ValueKind.Should().Be(JsonValueKind.Null);
        local.GetProperty("confidence").GetDouble().Should().Be(0.86);
        local.GetProperty("verified").GetBoolean().Should().BeTrue();
        local.GetProperty("matchedLines").EnumerateArray().Select(line => line.GetString())
            .Should().Equal("Is this the real life?", "Is this just fantasy?");
        local.GetProperty("versionCount").GetInt32().Should().Be(2);

        JsonElement external = root.GetProperty("guesses")[1];
        external.GetProperty("source").GetString().Should().Be("NetEase");
        external.GetProperty("spotifyTrackId").ValueKind.Should().Be(JsonValueKind.Null);
        external.GetProperty("durationMs").GetInt32().Should().Be(SONG_DURATION_MS);
        external.GetProperty("verified").GetBoolean().Should().BeFalse();

        JsonElement netease = root.GetProperty("providers")[0];
        netease.GetProperty("name").GetString().Should().Be("NetEase");
        netease.GetProperty("status").GetString().Should().Be("ok");
        netease.GetProperty("retryAfterSeconds").ValueKind.Should().Be(JsonValueKind.Null);

        JsonElement lrclib = root.GetProperty("providers")[1];
        lrclib.GetProperty("status").GetString().Should().Be("rateLimited");
        lrclib.GetProperty("retryAfterSeconds").GetInt32().Should().Be(RETRY_AFTER_SECONDS);
    }

    [Fact]
    public async Task Search_AfterTheLoweredPermitLimit_ReturnsTooManyRequestsEvenWithoutCredential()
    {
        HttpClient client = CreateFactory(configureLookup: o => o.SearchPermitLimit = LOWERED_PERMIT_LIMIT)
            .CreateClient();

        List<HttpStatusCode> statuses = new();
        for (int i = 0; i <= LOWERED_PERMIT_LIMIT; i++)
        {
            HttpResponseMessage response = await client.SendAsync(
                Post(SEARCH_PATH, new { fragment = FRAGMENT }, sessionId: null));
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests);

        HttpResponseMessage play = await client.SendAsync(
            Post(PLAY_PATH, new { action = "play", spotifyTrackId = TRACK_ID }, sessionId: null));
        play.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "plays have their own window");
    }

    [Fact]
    public async Task Play_AfterTheLoweredPermitLimit_ReturnsTooManyRequests()
    {
        SetupPlayResult(new SongPlayResult(SongPlayStatus.Success));
        HttpClient client = CreateFactory(configureLookup: o => o.PlayPermitLimit = LOWERED_PERMIT_LIMIT)
            .CreateClient();

        List<HttpStatusCode> statuses = new();
        for (int i = 0; i <= LOWERED_PERMIT_LIMIT; i++)
        {
            HttpResponseMessage response = await client.SendAsync(
                Post(PLAY_PATH, new { action = "queue", spotifyTrackId = TRACK_ID }));
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"spotifyTrackId\":\"4u7EnebtmKWzUH433cf5Qv\"}")]
    [InlineData("{\"action\":\"skip\",\"spotifyTrackId\":\"4u7EnebtmKWzUH433cf5Qv\"}")]
    [InlineData("{\"action\":\"play\"}")]
    [InlineData("{\"action\":\"play\",\"spotifyTrackId\":\"not-a-track-id\"}")]
    [InlineData("{\"action\":\"play\",\"title\":\"Bohemian Rhapsody\"}")]
    [InlineData("{\"action\":\"queue\",\"title\":\" \",\"artist\":\"Queen\"}")]
    public async Task Play_WithInvalidBody_ReturnsBadRequestAndDoesNotPlay(string json)
    {
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(PostRaw(PLAY_PATH, json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNoServiceCalled();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Play_WithATitleOrArtistOverTheCap_ReturnsBadRequestAndDoesNotPlay(bool longTitle)
    {
        HttpClient client = CreateFactory().CreateClient();
        string tooLong = new('a', LyricMatchConstants.MAX_NAME_CHARACTERS + 1);
        string json = JsonSerializer.Serialize(new
        {
            action = "play",
            title = longTitle ? tooLong : "Bohemian Rhapsody",
            artist = longTitle ? "Queen" : tooLong,
        });

        HttpResponseMessage response = await client.SendAsync(PostRaw(PLAY_PATH, json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNoServiceCalled();
    }

    [Fact]
    public async Task Play_ById_PassesTheOwnersTokenAndReturnsSuccess()
    {
        SetupPlayResult(new SongPlayResult(SongPlayStatus.Success));
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            Post(PLAY_PATH, new { action = "play", spotifyTrackId = TRACK_ID }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("success");
        document.RootElement.GetProperty("track").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("retryAfterSeconds").ValueKind.Should().Be(JsonValueKind.Null);
        _playbackService.Verify(
            s => s.PlayAsync(
                SESSION_ID,
                ACCESS_TOKEN,
                new SongPlayRequest(SongPlayAction.Play, TRACK_ID, null, null, null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Play_ByTitleAndArtist_ReturnsTheResolvedTrack()
    {
        SetupPlayResult(new SongPlayResult(
            SongPlayStatus.Success,
            new TrackInfo
            {
                Id = RESOLVED_TRACK_ID,
                Title = "Bohemian Rhapsody - Remastered 2011",
                Artist = "Queen",
                Duration = TimeSpan.FromMilliseconds(SONG_DURATION_MS)
            }));
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(Post(
            PLAY_PATH,
            new { action = "queue", title = "Bohemian Rhapsody", artist = "Queen", durationMs = SONG_DURATION_MS }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("success");
        document.RootElement.GetProperty("track").GetProperty("id").GetString().Should().Be(RESOLVED_TRACK_ID);
        _playbackService.Verify(
            s => s.PlayAsync(
                SESSION_ID,
                ACCESS_TOKEN,
                new SongPlayRequest(SongPlayAction.Queue, null, "Bohemian Rhapsody", "Queen", SONG_DURATION_MS),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Play_WhenNotOnSpotify_ReturnsOkWithNotOnSpotifyStatus()
    {
        SetupPlayResult(new SongPlayResult(SongPlayStatus.NotOnSpotify));
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            Post(PLAY_PATH, new { action = "play", title = "Unknown Song", artist = "Nobody" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("notOnSpotify");
    }

    [Fact]
    public async Task Play_WhenSpotifyIsRateLimited_ReturnsOkWithRetryAfterSeconds()
    {
        SetupPlayResult(new SongPlayResult(
            SongPlayStatus.RateLimited,
            RetryAfter: TimeSpan.FromSeconds(RETRY_AFTER_SECONDS)));
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage response = await client.SendAsync(
            Post(PLAY_PATH, new { action = "play", spotifyTrackId = TRACK_ID }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("rateLimited");
        document.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().Be(RETRY_AFTER_SECONDS);
    }

    [Theory]
    [InlineData("play", true)]
    [InlineData("queue", false)]
    public async Task Play_WithTheRealPlayService_RequestsAFollowUpPollOnlyAfterPlay(string action, bool expectPoll)
    {
        _playerClient
            .Setup(p => p.PlayTrackAsync(ACCESS_TOKEN, $"spotify:track:{TRACK_ID}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
        _playerClient
            .Setup(p => p.AddToQueueAsync(ACCESS_TOKEN, $"spotify:track:{TRACK_ID}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerCommandResult.Success);
        WebApplicationFactory<Program> factory = CreateFactory(realPlayback: true);
        HttpClient client = factory.CreateClient();
        IPlaybackSessionRegistry registry = factory.Services.GetRequiredService<IPlaybackSessionRegistry>();
        List<string> pollRequests = new();
        registry.OnUserActivityRequested += (_, userId) =>
        {
            lock (pollRequests)
            {
                pollRequests.Add(userId);
            }
        };

        HttpResponseMessage response = await client.SendAsync(
            Post(PLAY_PATH, new { action, spotifyTrackId = TRACK_ID }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("success");
        lock (pollRequests)
        {
            if (expectPoll)
            {
                pollRequests.Should().Contain(SESSION_ID);
            }
            else
            {
                pollRequests.Should().BeEmpty();
            }
        }
    }
}
