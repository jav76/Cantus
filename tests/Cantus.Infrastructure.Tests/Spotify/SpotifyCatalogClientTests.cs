using System.Net;
using System.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public sealed class SpotifyCatalogClientTests
{
    private const int RETRY_AFTER_SECONDS = 42;
    private const int SEARCH_LIMIT = 5;
    private const int TRACK_DURATION_MS = 354_320;
    private const string TRACK_ID = "4u7EnebtmKWzUH433cf5Qv";

    private const string SEARCH_RESPONSE_JSON = """
        {
          "tracks": {
            "href": "https://api.spotify.com/v1/search",
            "limit": 5,
            "offset": 0,
            "total": 1,
            "items": [
              {
                "id": "4u7EnebtmKWzUH433cf5Qv",
                "name": "Bohemian Rhapsody - Remastered 2011",
                "duration_ms": 354320,
                "explicit": false,
                "type": "track",
                "uri": "spotify:track:4u7EnebtmKWzUH433cf5Qv",
                "artists": [ { "id": "1dfeR4HaWDbWqFHLkxsg1d", "name": "Queen", "type": "artist" } ],
                "album": { "id": "6X9k3hSsvQck2OfKYdBbXr", "name": "A Night At The Opera", "images": [] }
              }
            ]
          }
        }
        """;

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        public List<Uri> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (ResponseHandler is not null)
            {
                return Task.FromResult(ResponseHandler(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly SpotifyRateLimitGate _gate;
    private readonly MockHttpMessageHandler _handler = new();
    private readonly SpotifyCatalogClient _client;

    public SpotifyCatalogClientTests()
    {
        _gate = new SpotifyRateLimitGate(_timeProvider);
        _client = new SpotifyCatalogClient(
            NullLogger<SpotifyCatalogClient>.Instance,
            accessToken => new SpotifyClient(
                SpotifyClientConfig
                    .CreateDefault(accessToken)
                    .WithHTTPClient(new NetHttpClient(new HttpClient(_handler)))),
            _gate);
    }

    [Fact]
    public async Task SearchTracksAsync_SendsATrackSearchWithLimitAndMarketAndMapsTheTracks()
    {
        _handler.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SEARCH_RESPONSE_JSON, Encoding.UTF8, "application/json")
        };

        IReadOnlyList<TrackInfo> tracks = await _client.SearchTracksAsync(
            "some-token",
            "track:\"bohemian rhapsody\" artist:\"queen\"",
            SEARCH_LIMIT);

        _handler.RequestUris.Should().ContainSingle();
        Uri uri = _handler.RequestUris[0];
        uri.AbsolutePath.Should().Be("/v1/search");
        string query = Uri.UnescapeDataString(uri.Query.Replace('+', ' '));
        query.Should().Contain("type=track");
        query.Should().Contain("limit=5");
        query.Should().Contain("market=from_token");
        query.Should().Contain("track:\"bohemian rhapsody\" artist:\"queen\"");

        tracks.Should().ContainSingle();
        tracks[0].Id.Should().Be(TRACK_ID);
        tracks[0].Title.Should().Be("Bohemian Rhapsody - Remastered 2011");
        tracks[0].Artist.Should().Be("Queen");
        tracks[0].Album.Should().Be("A Night At The Opera");
        tracks[0].Duration.Should().Be(TimeSpan.FromMilliseconds(TRACK_DURATION_MS));
    }

    [Fact]
    public async Task SearchTracksAsync_WhenSpotifyReturns429_ReportsTheSharedGateAndThrows()
    {
        _handler.ResponseHandler = _ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", RETRY_AFTER_SECONDS.ToString());
            return response;
        };

        Func<Task> act = () => _client.SearchTracksAsync("some-token", "queen", SEARCH_LIMIT);

        SpotifyRateLimitedException thrown = (await act.Should().ThrowAsync<SpotifyRateLimitedException>()).Which;
        thrown.RetryAfter.Should().Be(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
        _gate.IsLimited.Should().BeTrue();
        _gate.Remaining.Should().Be(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
    }

    [Fact]
    public async Task SearchTracksAsync_WhileTheGateIsLimited_SendsNothing()
    {
        _gate.Report(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));

        Func<Task> act = () => _client.SearchTracksAsync("some-token", "queen", SEARCH_LIMIT);

        await act.Should().ThrowAsync<SpotifyRateLimitedException>();
        _handler.RequestUris.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "queen")]
    [InlineData("some-token", " ")]
    public async Task SearchTracksAsync_WithABlankTokenOrQuery_ReturnsNothingWithoutCallingSpotify(
        string token,
        string query)
    {
        IReadOnlyList<TrackInfo> tracks = await _client.SearchTracksAsync(token, query, SEARCH_LIMIT);

        tracks.Should().BeEmpty();
        _handler.RequestUris.Should().BeEmpty();
    }
}
