using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LyricsCorrectionServiceTests
{
    private const string SERVER_BASE_URL = "http://cantus.test:5000";
    private const string SESSION_TOKEN = "sess-owner";
    private const string TRACK_ID = "4cOdK2wGLETKBW3PvgPWqT";

    private const string CANDIDATES_JSON = """
        {
          "trackId": "4cOdK2wGLETKBW3PvgPWqT",
          "candidates": [
            {
              "provider": "LRCLIB",
              "providerCandidateId": "3396226",
              "title": "Never Gonna Give You Up",
              "artist": "Rick Astley",
              "album": "Whenever You Need Somebody",
              "durationMs": 213000,
              "isSynced": true,
              "isInstrumental": false,
              "preview": ["We're no strangers to love", "You know the rules and so do I"]
            }
          ],
          "unavailableProviders": ["NetEase"],
          "activeOverride": { "kind": "candidate", "provider": "LRCLIB", "candidateId": "3396226" }
        }
        """;

    private const string LYRICS_JSON = """
        {
          "trackId": "4cOdK2wGLETKBW3PvgPWqT",
          "title": "Never Gonna Give You Up",
          "artist": "Rick Astley",
          "isSynced": true,
          "isInstrumental": false,
          "lines": [ { "timestampMs": 18450, "text": "We're no strangers to love", "syllables": null } ]
        }
        """;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string?> Bodies { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return Respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static (RecordingHandler Handler, LyricsCorrectionService Service) CreateService(string? sessionToken)
    {
        Mock<ISignalRPlaybackClient> client = new();
        client.SetupGet(c => c.ServerBaseUrl).Returns(SERVER_BASE_URL);
        client.SetupGet(c => c.SessionToken).Returns(sessionToken);
        RecordingHandler handler = new();
        return (handler, new LyricsCorrectionService(client.Object, new HttpClient(handler)));
    }

    [Fact]
    public async Task GetCandidatesAsync_RequestsTheTracksCandidatesWithTheSessionToken()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, CANDIDATES_JSON);

        // Act
        await service.GetCandidatesAsync(TRACK_ID);

        // Assert
        HttpRequestMessage request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri($"{SERVER_BASE_URL}/api/lyrics/{TRACK_ID}/candidates"));
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(SESSION_TOKEN);
    }

    [Fact]
    public async Task GetCandidatesAsync_WithoutASessionToken_SendsNoAuthorizationHeader()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(null);
        handler.Respond = _ => Json(HttpStatusCode.OK, CANDIDATES_JSON);

        // Act
        await service.GetCandidatesAsync(TRACK_ID);

        // Assert
        handler.Requests.Should().ContainSingle().Which.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidatesAsync_EscapesTheTrackId()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, CANDIDATES_JSON);

        // Act
        await service.GetCandidatesAsync("a/b c");

        // Assert
        handler.Requests.Should().ContainSingle()
            .Which.RequestUri!.AbsoluteUri.Should().Be($"{SERVER_BASE_URL}/api/lyrics/a%2Fb%20c/candidates");
    }

    [Fact]
    public async Task GetCandidatesAsync_ReadsEveryCandidateFieldAndTheActiveOverride()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, CANDIDATES_JSON);

        // Act
        LyricsCandidatesResult result = await service.GetCandidatesAsync(TRACK_ID);

        // Assert
        result.Status.Should().Be(LyricsCorrectionStatus.Succeeded);
        LyricsCandidatesPayload payload = result.Candidates!;
        payload.TrackId.Should().Be(TRACK_ID);
        payload.UnavailableProviders.Should().Equal("NetEase");
        LyricsCandidatePayload candidate = payload.Candidates.Should().ContainSingle().Subject;
        candidate.Provider.Should().Be("LRCLIB");
        candidate.ProviderCandidateId.Should().Be("3396226");
        candidate.Title.Should().Be("Never Gonna Give You Up");
        candidate.Artist.Should().Be("Rick Astley");
        candidate.Album.Should().Be("Whenever You Need Somebody");
        candidate.DurationMs.Should().Be(213000);
        candidate.IsSynced.Should().BeTrue();
        candidate.IsInstrumental.Should().BeFalse();
        candidate.Preview.Should().Equal("We're no strangers to love", "You know the rules and so do I");
        payload.ActiveOverride!.Kind.Should().Be(LyricsActiveOverridePayload.KIND_CANDIDATE);
        payload.ActiveOverride.Provider.Should().Be("LRCLIB");
        payload.ActiveOverride.CandidateId.Should().Be("3396226");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LyricsCorrectionStatus.SignedOut)]
    [InlineData(HttpStatusCode.Forbidden, LyricsCorrectionStatus.NotAllowed)]
    [InlineData(HttpStatusCode.NotFound, LyricsCorrectionStatus.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, LyricsCorrectionStatus.ProviderUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, LyricsCorrectionStatus.Rejected)]
    [InlineData(HttpStatusCode.MethodNotAllowed, LyricsCorrectionStatus.ServerOutdated)]
    [InlineData(HttpStatusCode.InternalServerError, LyricsCorrectionStatus.Failed)]
    public async Task GetCandidatesAsync_MapsErrorStatuses(HttpStatusCode code, LyricsCorrectionStatus expected)
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(code, """{ "message": "nope" }""");

        // Act
        LyricsCandidatesResult result = await service.GetCandidatesAsync(TRACK_ID);

        // Assert
        result.Status.Should().Be(expected);
        result.Candidates.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidatesAsync_WhenTheServerCannotBeReached_ReportsFailed()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => throw new HttpRequestException("connection refused");

        // Act
        LyricsCandidatesResult result = await service.GetCandidatesAsync(TRACK_ID);

        // Assert
        result.Status.Should().Be(LyricsCorrectionStatus.Failed);
    }

    [Fact]
    public async Task GetCandidatesAsync_WithAMalformedBody_ReportsFailed()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, "<html>");

        // Act
        LyricsCandidatesResult result = await service.GetCandidatesAsync(TRACK_ID);

        // Assert
        result.Status.Should().Be(LyricsCorrectionStatus.Failed);
    }

    [Fact]
    public async Task ApplyOverrideAsync_PostsTheChoiceAndReturnsTheLyricsNowInEffect()
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, LYRICS_JSON);

        // Act
        LyricsOverrideResult result = await service.ApplyOverrideAsync(
            TRACK_ID,
            LyricsOverrideRequestPayload.ForCandidate("LRCLIB", "3396226"));

        // Assert
        HttpRequestMessage request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri($"{SERVER_BASE_URL}/api/lyrics/{TRACK_ID}/override"));
        request.Headers.Authorization!.Parameter.Should().Be(SESSION_TOKEN);

        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]!);
        body.RootElement.GetProperty("action").GetString().Should().Be("candidate");
        body.RootElement.GetProperty("provider").GetString().Should().Be("LRCLIB");
        body.RootElement.GetProperty("candidateId").GetString().Should().Be("3396226");

        result.Status.Should().Be(LyricsCorrectionStatus.Succeeded);
        result.Lyrics!.TrackId.Should().Be(TRACK_ID);
        result.Lyrics.Lines.Should().ContainSingle().Which.Text.Should().Be("We're no strangers to love");
    }

    [Fact]
    public async Task ApplyOverrideAsync_KeepsWordTimingSoKaraokeFillStillFollowsEachWord()
    {
        // Arrange
        const string wordTimedJson = """
            {
              "trackId": "4cOdK2wGLETKBW3PvgPWqT",
              "isSynced": true,
              "lines": [
                {
                  "timestampMs": 18450,
                  "text": "We're no strangers",
                  "syllables": [
                    { "timestampMs": 18450, "durationMs": 400, "text": "We're " },
                    { "timestampMs": 18850, "durationMs": 300, "text": "no " },
                    { "timestampMs": 19150, "durationMs": 900, "text": "strangers" }
                  ]
                }
              ]
            }
            """;
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, wordTimedJson);

        // Act
        LyricsOverrideResult result = await service.ApplyOverrideAsync(
            TRACK_ID,
            LyricsOverrideRequestPayload.ForCandidate("NetEase", "111"));

        // Assert
        LyricLinePayload line = result.Lyrics!.Lines.Should().ContainSingle().Subject;
        line.Syllables.Should().NotBeNull();
        line.Syllables!.Select(s => (s.TimestampMs, s.DurationMs, s.Text)).Should().Equal(
            (18450L, 400L, "We're "),
            (18850L, 300L, "no "),
            (19150L, 900L, "strangers"));
    }

    [Theory]
    [InlineData("refetch")]
    [InlineData("noLyrics")]
    [InlineData("clear")]
    public async Task ApplyOverrideAsync_SendsActionsWithoutACandidateAsJustTheAction(string action)
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(HttpStatusCode.OK, LYRICS_JSON);
        LyricsOverrideRequestPayload request = action switch
        {
            "refetch" => LyricsOverrideRequestPayload.Refetch(),
            "noLyrics" => LyricsOverrideRequestPayload.NoLyrics(),
            _ => LyricsOverrideRequestPayload.Clear()
        };

        // Act
        await service.ApplyOverrideAsync(TRACK_ID, request);

        // Assert
        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]!);
        body.RootElement.GetProperty("action").GetString().Should().Be(action);
        body.RootElement.TryGetProperty("provider", out JsonElement provider).Should().BeTrue();
        provider.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LyricsCorrectionStatus.SignedOut)]
    [InlineData(HttpStatusCode.Forbidden, LyricsCorrectionStatus.NotAllowed)]
    [InlineData(HttpStatusCode.NotFound, LyricsCorrectionStatus.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, LyricsCorrectionStatus.ProviderUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, LyricsCorrectionStatus.Rejected)]
    [InlineData(HttpStatusCode.MethodNotAllowed, LyricsCorrectionStatus.ServerOutdated)]
    public async Task ApplyOverrideAsync_MapsErrorStatuses(HttpStatusCode code, LyricsCorrectionStatus expected)
    {
        // Arrange
        (RecordingHandler handler, LyricsCorrectionService service) = CreateService(SESSION_TOKEN);
        handler.Respond = _ => Json(code, """{ "message": "nope" }""");

        // Act
        LyricsOverrideResult result = await service.ApplyOverrideAsync(TRACK_ID, LyricsOverrideRequestPayload.Clear());

        // Assert
        result.Status.Should().Be(expected);
        result.Lyrics.Should().BeNull();
    }
}
