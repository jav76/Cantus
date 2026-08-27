using System.Net;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public class CachedLyricsServiceTests
{
    private readonly ILyricsCacheRepository _mockRepo;
    private readonly LrclibLyricsProvider _lrclibProvider;
    private readonly CachedLyricsService _service;

    public CachedLyricsServiceTests()
    {
        _mockRepo = Substitute.For<ILyricsCacheRepository>();

        HttpClient httpClient = new(new DummyHttpMessageHandler())
        {
            BaseAddress = new Uri("https://lrclib.net")
        };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions { NegativeCacheDays = 7 });
        _lrclibProvider = Substitute.ForPartsOf<LrclibLyricsProvider>(
            httpClient,
            options,
            NullLogger<LrclibLyricsProvider>.Instance);

        _service = new CachedLyricsService(
            _mockRepo,
            _lrclibProvider,
            options,
            NullLogger<CachedLyricsService>.Instance);
    }

    [Fact]
    public async Task GetLyricsAsync_WhenNegativeCached_ReturnsNullWithoutCallingProvider()
    {
        TrackInfo track = new() { Id = "t1", Title = "Song", Artist = "Artist" };
        _mockRepo.IsMarkedNotFoundAsync("t1").Returns(true);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeNull();
        await _mockRepo.DidNotReceive().GetCachedLyricsAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCached_ReturnsCachedWithoutCallingProvider()
    {
        TrackInfo track = new() { Id = "t2", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = new() { TrackId = "t2", Title = "Song", Artist = "Artist", Lines = [] };

        _mockRepo.IsMarkedNotFoundAsync("t2").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t2").Returns(cached);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _mockRepo.DidNotReceive().SaveLyricsAsync(Arg.Any<SyncedLyrics>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCacheMiss_AndFoundOnLrclib_SavesToCacheAndReturns()
    {
        TrackInfo track = new() { Id = "t3", Title = "Song", Artist = "Artist" };
        SyncedLyrics fresh = new()
        {
            TrackId = "t3",
            Title = "Song",
            Artist = "Artist",
            Lines = [new(TimeSpan.Zero, "Hi")]
        };

        _mockRepo.IsMarkedNotFoundAsync("t3").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t3").Returns((SyncedLyrics?)null);
        _lrclibProvider.GetLyricsAsync(track).Returns(fresh);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(fresh);
        await _mockRepo.Received(1).SaveLyricsAsync(fresh, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCacheMiss_AndNotFoundOnLrclib_MarksNotFoundInCache()
    {
        TrackInfo track = new()
        {
            Id = "t4",
            Title = "Missing",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(180)
        };

        _mockRepo.IsMarkedNotFoundAsync("t4").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t4").Returns((SyncedLyrics?)null);
        _lrclibProvider.GetLyricsAsync(track).Returns((SyncedLyrics?)null);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeNull();
        await _mockRepo.Received(1).MarkNotFoundAsync(
            "t4",
            "Missing",
            "Artist",
            "",
            180000,
            TimeSpan.FromDays(7),
            Arg.Any<CancellationToken>());
    }

    private sealed class DummyHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
