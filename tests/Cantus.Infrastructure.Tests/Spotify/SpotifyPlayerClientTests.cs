using System.Net;
using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public class SpotifyPlayerClientTests
{
    private const int RETRY_AFTER_SECONDS = 42;
    private const string TRACK_URI = "spotify:track:4u7EnebtmKWzUH433cf5Qv";

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        public int RequestCount { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (ResponseHandler is not null)
            {
                return ResponseHandler(request);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static SpotifyPlayerClient CreateClient(
        MockHttpMessageHandler handler,
        SpotifyRateLimitGate gate)
    {
        return new SpotifyPlayerClient(
            NullLogger<SpotifyPlayerClient>.Instance,
            accessToken => new SpotifyClient(
                SpotifyClientConfig
                    .CreateDefault(accessToken)
                    .WithHTTPClient(new NetHttpClient(new HttpClient(handler)))),
            gate);
    }

    [Fact]
    public async Task PlayerCommand_WhenSpotifyReturns429_ReportsTheSharedGateAndFails()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        SpotifyRateLimitGate gate = new(timeProvider);
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ =>
            {
                HttpResponseMessage response = new((HttpStatusCode)429);
                response.Headers.Add("Retry-After", RETRY_AFTER_SECONDS.ToString());
                return response;
            }
        };
        SpotifyPlayerClient client = CreateClient(handler, gate);

        PlayerCommandResult result = await client.PausePlaybackAsync("some-token");

        result.Should().Be(PlayerCommandResult.Failed);
        handler.RequestCount.Should().Be(1);
        gate.IsLimited.Should().BeTrue();
        gate.Remaining.Should().Be(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS));
    }

    [Fact]
    public async Task PlayerCommand_WhenSpotifyReturns404_DoesNotReportTheGate()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        SpotifyRateLimitGate gate = new(timeProvider);
        MockHttpMessageHandler handler = new();
        SpotifyPlayerClient client = CreateClient(handler, gate);

        PlayerCommandResult result = await client.SeekPlaybackAsync("some-token", 1000);

        result.Should().Be(PlayerCommandResult.NoActiveDevice);
        gate.IsLimited.Should().BeFalse();
    }

    [Fact]
    public async Task PlayTrackAsync_SendsThePlayRequestWithTheTrackUri()
    {
        SpotifyRateLimitGate gate = new(TimeProvider.System);
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NoContent)
        };
        SpotifyPlayerClient client = CreateClient(handler, gate);

        PlayerCommandResult result = await client.PlayTrackAsync("some-token", TRACK_URI);

        result.Should().Be(PlayerCommandResult.Success);
        handler.LastMethod.Should().Be(HttpMethod.Put);
        handler.LastUri!.AbsolutePath.Should().Be("/v1/me/player/play");
        handler.LastBody.Should().Contain("\"uris\"").And.Contain(TRACK_URI);
    }

    [Fact]
    public async Task AddToQueueAsync_SendsTheQueueRequestWithTheTrackUri()
    {
        SpotifyRateLimitGate gate = new(TimeProvider.System);
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NoContent)
        };
        SpotifyPlayerClient client = CreateClient(handler, gate);

        PlayerCommandResult result = await client.AddToQueueAsync("some-token", TRACK_URI);

        result.Should().Be(PlayerCommandResult.Success);
        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastUri!.AbsolutePath.Should().Be("/v1/me/player/queue");
        Uri.UnescapeDataString(handler.LastUri.Query).Should().Contain("uri=" + TRACK_URI);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PlayerCommandResult.NoActiveDevice)]
    [InlineData(HttpStatusCode.Forbidden, PlayerCommandResult.MissingPermissions)]
    public async Task PlayTrackAndQueue_MapSpotifyErrorsToCommandResults(
        HttpStatusCode statusCode,
        PlayerCommandResult expected)
    {
        SpotifyRateLimitGate gate = new(TimeProvider.System);
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(statusCode)
        };
        SpotifyPlayerClient client = CreateClient(handler, gate);

        (await client.PlayTrackAsync("some-token", TRACK_URI)).Should().Be(expected);
        (await client.AddToQueueAsync("some-token", TRACK_URI)).Should().Be(expected);
        gate.IsLimited.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetCurrentPlaybackAsync_WithInvalidToken_ReturnsNull(string? token)
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        Core.Models.PlaybackState? result = await client.GetCurrentPlaybackAsync(token!);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PlayerCommands_WithInvalidToken_FailWithoutCallingSpotify(string token)
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        (await client.PausePlaybackAsync(token)).Should().Be(PlayerCommandResult.Failed);
        (await client.SetVolumeAsync(token, 50)).Should().Be(PlayerCommandResult.Failed);
        (await client.SeekPlaybackAsync(token, 1000)).Should().Be(PlayerCommandResult.Failed);
        (await client.SetShuffleAsync(token, true)).Should().Be(PlayerCommandResult.Failed);
        (await client.SetRepeatAsync(token, "context")).Should().Be(PlayerCommandResult.Failed);
        (await client.PlayTrackAsync(token, TRACK_URI)).Should().Be(PlayerCommandResult.Failed);
        (await client.AddToQueueAsync(token, TRACK_URI)).Should().Be(PlayerCommandResult.Failed);
    }

    [Fact]
    public async Task SetRepeatAsync_WithUnknownMode_FailsWithoutCallingSpotify()
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        PlayerCommandResult result = await client.SetRepeatAsync("some-token", "shuffle-all");

        result.Should().Be(PlayerCommandResult.Failed);
    }
}
