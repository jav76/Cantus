using System.Net;
using System.Net.Http.Headers;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public class ProviderRateGateHandlerTests
{
    private sealed class FakeInnerHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseHandler { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(ResponseHandler(request));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
        }
    }

    private static readonly TrackInfo Track = new()
    {
        Id = "spotify_track",
        Title = "Some Song",
        Artist = "Some Artist",
        Duration = TimeSpan.FromSeconds(200)
    };

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeInnerHandler _inner = new();
    private readonly ProviderRateGate _gate;

    public ProviderRateGateHandlerTests()
    {
        _gate = new ProviderRateGate(
            _timeProvider,
            Options.Create(new SongLookupOptions { NeteaseRequestsPerMinute = 1 }),
            NullLogger<ProviderRateGate>.Instance);
    }

    private HttpClient CreateClient(string providerName, string baseUrl) =>
        new(new ProviderRateGateHandler(providerName, _gate) { InnerHandler = _inner })
        {
            BaseAddress = new Uri(baseUrl)
        };

    private static HttpRequestMessage LookupRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        ProviderRequestOptions.MarkAsLookup(request);
        return request;
    }

    private static HttpResponseMessage TooManyRequests(int retryAfterSeconds)
    {
        HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }

    [Fact]
    public void LookupTag_IsReadBackFromTheRequest()
    {
        using HttpRequestMessage untagged = new(HttpMethod.Get, "https://lrclib.net/api/get");
        using HttpRequestMessage tagged = LookupRequest("https://lrclib.net/api/search");

        ProviderRequestOptions.IsLookupRequest(untagged).Should().BeFalse();
        ProviderRequestOptions.IsLookupRequest(tagged).Should().BeTrue();
    }

    [Fact]
    public async Task Lrclib429_ThrowsWithoutSendingUntilTheClockAdvances()
    {
        using HttpClient client = CreateClient(LrclibLyricsProvider.PROVIDER_NAME, "https://lrclib.net");
        _inner.ResponseHandler = _ => TooManyRequests(20);

        using HttpResponseMessage first = await client.GetAsync("/api/get");
        first.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        _inner.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK);
        Func<Task> blocked = () => client.GetAsync("/api/get");
        (await blocked.Should().ThrowAsync<ProviderRateLimitedException>())
            .Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(20));
        _inner.RequestCount.Should().Be(1);

        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        using HttpResponseMessage resumed = await client.GetAsync("/api/get");
        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        _inner.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task Lrclib404_NeverStartsACooldown()
    {
        using HttpClient client = CreateClient(LrclibLyricsProvider.PROVIDER_NAME, "https://lrclib.net");

        using HttpResponseMessage miss = await client.GetAsync("/api/get");
        using HttpResponseMessage again = await client.GetAsync("/api/get");

        miss.StatusCode.Should().Be(HttpStatusCode.NotFound);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _inner.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task NeteaseLookupThrottle_BlocksLookupsButNotNowPlaying()
    {
        using HttpClient client = CreateClient(NeteaseLyricsProvider.PROVIDER_NAME, "https://music.163.com");
        _inner.ResponseHandler = _ => TooManyRequests(30);

        using HttpResponseMessage throttled = await client.SendAsync(LookupRequest("/api/search/get"));

        _inner.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK);
        Func<Task> lookup = () => client.SendAsync(LookupRequest("/api/search/get"));
        await lookup.Should().ThrowAsync<ProviderRateLimitedException>();

        using HttpResponseMessage nowPlaying = await client.GetAsync("/api/search/get");
        nowPlaying.StatusCode.Should().Be(HttpStatusCode.OK);
        _inner.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task SpentLookupBudget_NeverBlocksNowPlaying()
    {
        using HttpClient client = CreateClient(NeteaseLyricsProvider.PROVIDER_NAME, "https://music.163.com");
        _inner.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK);

        using HttpResponseMessage lookup = await client.SendAsync(LookupRequest("/api/search/get"));
        Func<Task> overBudget = () => client.SendAsync(LookupRequest("/api/search/get"));
        await overBudget.Should().ThrowAsync<ProviderRateLimitedException>();

        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage nowPlaying = await client.GetAsync("/api/search/get");
            nowPlaying.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _inner.RequestCount.Should().Be(6);
    }

    [Fact]
    public async Task LrclibFetch_DuringCooldown_ReturnsUnavailableWithoutSendingOrLoggingAnError()
    {
        _gate.ReportResponse(
            LrclibLyricsProvider.PROVIDER_NAME,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            null);
        RecordingLogger<LrclibLyricsProvider> logger = new();
        LrclibLyricsProvider provider = new(
            CreateClient(LrclibLyricsProvider.PROVIDER_NAME, "https://lrclib.net"),
            Options.Create(new LrclibOptions()),
            logger);

        LyricsFetchResult result = await provider.FetchLyricsAsync(Track);

        result.Should().Be(LyricsFetchResult.Unavailable());
        _inner.RequestCount.Should().Be(0);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task NeteaseFetch_DuringCooldown_ReturnsUnavailableWithoutSendingOrLoggingAWarning()
    {
        _gate.ReportResponse(
            NeteaseLyricsProvider.PROVIDER_NAME,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            null);
        RecordingLogger<NeteaseLyricsProvider> logger = new();
        NeteaseLyricsProvider provider = new(
            CreateClient(NeteaseLyricsProvider.PROVIDER_NAME, "https://music.163.com"),
            Options.Create(new NeteaseOptions()),
            logger);

        LyricsFetchResult result = await provider.FetchLyricsAsync(Track);

        result.Should().Be(LyricsFetchResult.Unavailable());
        _inner.RequestCount.Should().Be(0);
        logger.Levels.Should().NotContain(level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task LrclibCandidates_DuringCooldown_ThrowAsAnUnavailableProvider()
    {
        _gate.ReportResponse(
            LrclibLyricsProvider.PROVIDER_NAME,
            isLookup: false,
            HttpStatusCode.TooManyRequests,
            null);
        LrclibLyricsProvider provider = new(
            CreateClient(LrclibLyricsProvider.PROVIDER_NAME, "https://lrclib.net"),
            Options.Create(new LrclibOptions()),
            NullLogger<LrclibLyricsProvider>.Instance);

        Func<Task> act = () => provider.GetCandidatesAsync(Track);

        await act.Should().ThrowAsync<HttpRequestException>();
        _inner.RequestCount.Should().Be(0);
    }
}
