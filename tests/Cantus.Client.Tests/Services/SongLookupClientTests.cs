using System;
using System.Collections.Generic;
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

public sealed class SongLookupClientTests
{
    private const string SERVER_BASE_URL = "http://cantus.test:5000";
    private const string SESSION_TOKEN = "session-abc";
    private const string TRACK_ID = "4u7EnebtmKWzUH433cf5Qv";

    private const string SEARCH_RESPONSE_JSON = """
        {
          "outcome": "accepted",
          "onlineRecommended": true,
          "guesses": [{
            "source": "Local", "spotifyTrackId": "4u7EnebtmKWzUH433cf5Qv",
            "title": "Bohemian Rhapsody", "artist": "Queen", "album": "A Night at the Opera", "durationMs": null,
            "confidence": 0.86, "verified": true, "matchedLines": ["Is this the real life?"], "versionCount": 2
          }],
          "providers": [{ "name": "NetEase", "status": "rateLimited", "retryAfterSeconds": 42 }]
        }
        """;

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{}";
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task SearchAsync_WithToken_PostsToTheSearchRouteWithBearerAndBody()
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        handler.ResponseBody = SEARCH_RESPONSE_JSON;

        // Act
        await client.SearchAsync("is this the real life", SongLookupSearchRequestPayload.SCOPE_LOCAL);

        // Assert
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be($"{SERVER_BASE_URL}/api/lookup/search");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(SESSION_TOKEN);
        handler.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");

        using JsonDocument body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("fragment").GetString().Should().Be("is this the real life");
        body.RootElement.GetProperty("scope").GetString().Should().Be("local");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_WithoutToken_SendsNoAuthorizationHeaderButStillPosts(string? token)
    {
        // Arrange - on WASM the same-origin session cookie authenticates instead
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(token);
        handler.ResponseBody = SEARCH_RESPONSE_JSON;

        // Act
        SongLookupCallResult result = await client.SearchAsync("is this the real life", "auto");

        // Assert
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
        handler.LastRequest.Headers.Contains("Authorization").Should().BeFalse();
        result.Status.Should().Be(SongLookupCallStatus.Success);
    }

    [Fact]
    public async Task SearchAsync_ReadsTheServerUrlAndTokenPerRequest()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseBody = SEARCH_RESPONSE_JSON };
        Mock<ISignalRPlaybackClient> connection = new();
        SongLookupClient client = new(connection.Object, new HttpClient(handler));
        connection.SetupGet(c => c.ServerBaseUrl).Returns("http://later.test/");
        connection.SetupGet(c => c.SessionToken).Returns("later-token");

        // Act
        await client.SearchAsync("is this the real life", "auto");

        // Assert
        handler.LastRequest!.RequestUri!.ToString().Should().Be("http://later.test/api/lookup/search");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("later-token");
    }

    [Fact]
    public async Task SearchAsync_ParsesTheServerResponseShape()
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        handler.ResponseBody = SEARCH_RESPONSE_JSON;

        // Act
        SongLookupCallResult result = await client.SearchAsync("is this the real life", "local");

        // Assert
        result.Status.Should().Be(SongLookupCallStatus.Success);
        SongLookupResponsePayload response = result.Response!;
        response.Outcome.Should().Be(SongLookupResponsePayload.OUTCOME_ACCEPTED);
        response.OnlineRecommended.Should().BeTrue();
        SongGuessPayload guess = response.Guesses.Should().ContainSingle().Subject;
        guess.Source.Should().Be(SongGuessPayload.SOURCE_LOCAL);
        guess.SpotifyTrackId.Should().Be(TRACK_ID);
        guess.Title.Should().Be("Bohemian Rhapsody");
        guess.Album.Should().Be("A Night at the Opera");
        guess.DurationMs.Should().BeNull();
        guess.Confidence.Should().Be(0.86);
        guess.Verified.Should().BeTrue();
        guess.MatchedLines.Should().Equal("Is this the real life?");
        guess.VersionCount.Should().Be(2);
        SongLookupProviderPayload provider = response.Providers.Should().ContainSingle().Subject;
        provider.Name.Should().Be("NetEase");
        provider.Status.Should().Be(SongLookupProviderPayload.STATUS_RATE_LIMITED);
        provider.RetryAfterSeconds.Should().Be(42);
    }

    [Fact]
    public void SourceGeneratedMetadata_RoundTripsEveryPayload()
    {
        // Arrange
        SongLookupResponsePayload response = new()
        {
            Outcome = SongLookupResponsePayload.OUTCOME_AMBIGUOUS,
            OnlineRecommended = true,
            Guesses = new List<SongGuessPayload>
            {
                new()
                {
                    Source = "NetEase",
                    Title = "Song",
                    Artist = "Artist",
                    DurationMs = 354000,
                    Confidence = 0.55,
                    MatchedLines = new List<string> { "line one", "line two" },
                    VersionCount = 1,
                },
            },
            Providers = new List<SongLookupProviderPayload> { new() { Name = "LRCLIB", Status = "ok" } },
        };
        SongLookupPlayRequestPayload play = new()
        {
            Action = SongLookupPlayRequestPayload.ACTION_QUEUE,
            Title = "Song",
            Artist = "Artist",
            DurationMs = 354000,
        };
        SongLookupPlayResponsePayload played = new()
        {
            Status = SongLookupPlayResponsePayload.STATUS_SUCCESS,
            Track = new TrackInfoPayload { Id = TRACK_ID, Title = "Song", Artist = "Artist", DurationMs = 354000 },
        };

        // Act
        SongLookupResponsePayload? responseBack = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(response, CantusJsonContext.Default.SongLookupResponsePayload),
            CantusJsonContext.Default.SongLookupResponsePayload);
        SongLookupPlayRequestPayload? playBack = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(play, CantusJsonContext.Default.SongLookupPlayRequestPayload),
            CantusJsonContext.Default.SongLookupPlayRequestPayload);
        SongLookupPlayResponsePayload? playedBack = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(played, CantusJsonContext.Default.SongLookupPlayResponsePayload),
            CantusJsonContext.Default.SongLookupPlayResponsePayload);

        // Assert
        responseBack.Should().BeEquivalentTo(response);
        playBack.Should().Be(play);
        playedBack.Should().BeEquivalentTo(played);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, SongLookupCallStatus.Success)]
    [InlineData(HttpStatusCode.BadRequest, SongLookupCallStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, SongLookupCallStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, SongLookupCallStatus.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, SongLookupCallStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, SongLookupCallStatus.Failed)]
    [InlineData(HttpStatusCode.NotFound, SongLookupCallStatus.Failed)]
    public async Task SearchAsync_MapsHttpStatus(HttpStatusCode statusCode, SongLookupCallStatus expected)
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        handler.StatusCode = statusCode;
        handler.ResponseBody = statusCode == HttpStatusCode.OK ? SEARCH_RESPONSE_JSON : string.Empty;

        // Act
        SongLookupCallResult result = await client.SearchAsync("is this the real life", "auto");

        // Assert
        result.Status.Should().Be(expected);
        if (expected != SongLookupCallStatus.Success)
        {
            result.Response.Should().BeNull();
        }
    }

    [Fact]
    public async Task SearchAsync_BadRequest_CarriesTheServerMessage()
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        handler.StatusCode = HttpStatusCode.BadRequest;
        handler.ResponseBody = """{ "message": "Fragment is too short." }""";

        // Act
        SongLookupCallResult result = await client.SearchAsync("la", "auto");

        // Assert
        result.Status.Should().Be(SongLookupCallStatus.BadRequest);
        result.ErrorMessage.Should().Be("Fragment is too short.");
    }

    [Fact]
    public async Task SearchAsync_MalformedSuccessBody_IsFailed()
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        handler.ResponseBody = "not json";

        // Act
        SongLookupCallResult result = await client.SearchAsync("is this the real life", "auto");

        // Assert
        result.Status.Should().Be(SongLookupCallStatus.Failed);
    }

    [Fact]
    public async Task SearchAsync_WhenTheServerUrlIsMissing_IsFailedInsteadOfThrowing()
    {
        // Arrange - a loose connection mock returns null for every property
        MockHttpMessageHandler handler = new();
        SongLookupClient client = new(new Mock<ISignalRPlaybackClient>().Object, new HttpClient(handler));

        // Act
        SongLookupCallResult result = await client.SearchAsync("is this the real life", "auto");

        // Assert
        result.Status.Should().Be(SongLookupCallStatus.Failed);
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_WhenCancelled_Throws()
    {
        // Arrange
        (_, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        // Act
        Func<Task> act = () => client.SearchAsync("is this the real life", "auto", cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PlayAsync_PostsToThePlayRouteAndParsesTheResolvedTrack()
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(SESSION_TOKEN);
        handler.ResponseBody = """
            {
              "status": "success",
              "track": { "id": "4u7EnebtmKWzUH433cf5Qv", "title": "Song", "artist": "Artist", "durationMs": 354000 },
              "retryAfterSeconds": null
            }
            """;

        // Act
        SongPlayCallResult result = await client.PlayAsync(new SongLookupPlayRequestPayload
        {
            Action = SongLookupPlayRequestPayload.ACTION_PLAY,
            Title = "Song",
            Artist = "Artist",
            DurationMs = 354000,
        });

        // Assert
        handler.LastRequest!.RequestUri!.ToString().Should().Be($"{SERVER_BASE_URL}/api/lookup/play");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(SESSION_TOKEN);
        using JsonDocument body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("action").GetString().Should().Be("play");
        body.RootElement.GetProperty("title").GetString().Should().Be("Song");
        body.RootElement.GetProperty("artist").GetString().Should().Be("Artist");
        body.RootElement.GetProperty("durationMs").GetInt32().Should().Be(354000);

        result.Status.Should().Be(SongLookupCallStatus.Success);
        result.Response!.Status.Should().Be(SongLookupPlayResponsePayload.STATUS_SUCCESS);
        result.Response.Track!.Id.Should().Be(TRACK_ID);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, SongLookupCallStatus.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, SongLookupCallStatus.RateLimited)]
    public async Task PlayAsync_MapsHttpStatus(HttpStatusCode statusCode, SongLookupCallStatus expected)
    {
        // Arrange
        (MockHttpMessageHandler handler, SongLookupClient client) = CreateClient(null);
        handler.StatusCode = statusCode;
        handler.ResponseBody = string.Empty;

        // Act
        SongPlayCallResult result = await client.PlayAsync(new SongLookupPlayRequestPayload
        {
            Action = SongLookupPlayRequestPayload.ACTION_QUEUE,
            SpotifyTrackId = TRACK_ID,
        });

        // Assert
        result.Status.Should().Be(expected);
        result.Response.Should().BeNull();
    }

    private static (MockHttpMessageHandler Handler, SongLookupClient Client) CreateClient(string? token)
    {
        MockHttpMessageHandler handler = new();
        Mock<ISignalRPlaybackClient> connection = new();
        connection.SetupGet(c => c.ServerBaseUrl).Returns(SERVER_BASE_URL);
        connection.SetupGet(c => c.SessionToken).Returns(token);
        return (handler, new SongLookupClient(connection.Object, new HttpClient(handler)));
    }
}
