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
    private readonly ILyricsFetchProvider _primaryProvider;
    private readonly ILyricsFetchProvider _fallbackProvider;
    private readonly CachedLyricsService _service;

    public CachedLyricsServiceTests()
    {
        _mockRepo = Substitute.For<ILyricsCacheRepository>();
        _primaryProvider = Substitute.For<ILyricsFetchProvider>();
        _primaryProvider.ProviderName.Returns("Primary");
        _fallbackProvider = Substitute.For<ILyricsFetchProvider>();
        _fallbackProvider.ProviderName.Returns("Fallback");

        IOptions<LyricsCacheOptions> options = Options.Create(new LyricsCacheOptions
        {
            NegativeCacheDays = 7,
            WordTimingUpgradeMaxAttempts = 3,
            WordTimingUpgradeIntervalDays = 14
        });

        _service = new CachedLyricsService(
            _mockRepo,
            new[] { _primaryProvider, _fallbackProvider },
            options,
            NullLogger<CachedLyricsService>.Instance);
    }

    [Fact]
    public async Task GetLyricsAsync_WhenNegativeCached_ReturnsNullWithoutCallingProviders()
    {
        TrackInfo track = new() { Id = "t1", Title = "Song", Artist = "Artist" };
        _mockRepo.IsMarkedNotFoundAsync("t1").Returns(true);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeNull();
        await _mockRepo.DidNotReceive().GetCachedLyricsAsync(Arg.Any<string>());
        await _primaryProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCached_ReturnsCachedWithoutCallingProviders()
    {
        TrackInfo track = new() { Id = "t2", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = new() { TrackId = "t2", Title = "Song", Artist = "Artist", Lines = [] };

        _mockRepo.IsMarkedNotFoundAsync("t2").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t2").Returns(cached);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _primaryProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
        await _mockRepo.DidNotReceive().SaveLyricsAsync(Arg.Any<SyncedLyrics>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenPrimaryFindsWordTiming_SavesAndSkipsFallback()
    {
        TrackInfo track = new() { Id = "t3", Title = "Song", Artist = "Artist" };
        SyncedLyrics fresh = WordTimed("t3");

        _mockRepo.IsMarkedNotFoundAsync("t3").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t3").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(fresh));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(fresh);
        await _mockRepo.Received(1).SaveLyricsAsync(fresh, cancellationToken: Arg.Any<CancellationToken>());
        await _fallbackProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenPrimaryMisses_FallsBackAndSavesFallbackResult()
    {
        TrackInfo track = new() { Id = "t4", Title = "Song", Artist = "Artist" };
        SyncedLyrics fromFallback = new()
        {
            TrackId = "t4",
            Title = "Song",
            Artist = "Artist",
            Lines = [new(TimeSpan.Zero, "Hi from fallback")]
        };

        _mockRepo.IsMarkedNotFoundAsync("t4").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t4").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.NotFound());
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(fromFallback));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(fromFallback);
        await _mockRepo.Received(1).SaveLyricsAsync(fromFallback, cancellationToken: Arg.Any<CancellationToken>());
        await _mockRepo.DidNotReceive().MarkNotFoundAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenAllProvidersDefinitivelyMiss_MarksNotFoundInCache()
    {
        TrackInfo track = new()
        {
            Id = "t5",
            Title = "Missing",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(180)
        };

        _mockRepo.IsMarkedNotFoundAsync("t5").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t5").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.NotFound());
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.NotFound());

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeNull();
        await _mockRepo.Received(1).MarkNotFoundAsync(
            "t5",
            "Missing",
            "Artist",
            "",
            180000,
            TimeSpan.FromDays(7),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenAnyProviderUnavailable_DoesNotPoisonNegativeCache()
    {
        TrackInfo track = new()
        {
            Id = "t6",
            Title = "Song",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(200)
        };

        _mockRepo.IsMarkedNotFoundAsync("t6").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t6").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Unavailable());
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.NotFound());

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeNull();
        await _mockRepo.DidNotReceive().MarkNotFoundAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenAProviderThrows_ChainContinuesAndSkipsNegativeCache()
    {
        TrackInfo track = new() { Id = "t7", Title = "Song", Artist = "Artist" };
        SyncedLyrics fromFallback = new()
        {
            TrackId = "t7",
            Title = "Song",
            Artist = "Artist",
            Lines = [new(TimeSpan.Zero, "Recovered")]
        };

        _mockRepo.IsMarkedNotFoundAsync("t7").Returns(false);
        _mockRepo.GetCachedLyricsAsync("t7").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track)
            .Returns<Task<LyricsFetchResult>>(_ => throw new HttpRequestException("boom"));
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(fromFallback));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(fromFallback);
    }

    [Fact]
    public async Task GetLyricsAsync_WithCandidateOverride_ReturnsItOverNegativeCacheAndProviders()
    {
        TrackInfo track = new() { Id = "t8", Title = "Song", Artist = "Artist" };
        SyncedLyrics chosen = new()
        {
            TrackId = "t8",
            Title = "Song",
            Artist = "Artist",
            Lines = [new(TimeSpan.Zero, "Chosen")]
        };

        _mockRepo.GetLyricsOverrideAsync("t8").Returns(LyricsOverride.ForCandidate("Primary", "42", chosen));
        _mockRepo.IsMarkedNotFoundAsync("t8").Returns(true);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(chosen);
        await _primaryProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WithNoLyricsOverride_ReturnsNullWithoutCachingOrCallingProviders()
    {
        TrackInfo track = new() { Id = "t9", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = new() { TrackId = "t9", Title = "Song", Artist = "Artist", Lines = [] };

        _mockRepo.GetLyricsOverrideAsync("t9").Returns(LyricsOverride.ForNoLyrics("t9"));
        _mockRepo.GetCachedLyricsAsync("t9").Returns(cached);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeNull();
        await _primaryProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
        await _mockRepo.DidNotReceive().MarkNotFoundAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenPrimaryIsLineLevelAndFallbackHasWordTiming_PrefersWordTiming()
    {
        TrackInfo track = new() { Id = "t8", Title = "Song", Artist = "Artist" };
        SyncedLyrics lineLevel = LineLevel("t8");
        SyncedLyrics wordTimed = WordTimed("t8");

        _mockRepo.GetCachedLyricsAsync("t8").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(lineLevel));
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(wordTimed));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(wordTimed);
        await _mockRepo.Received(1).SaveLyricsAsync(wordTimed, cancellationToken: Arg.Any<CancellationToken>());
        await _mockRepo.DidNotReceive().SaveLyricsAsync(lineLevel, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetLyricsAsync_WhenOnlyLineLevelIsFound_KeepsTheFirstResult(bool fallbackUnavailable)
    {
        TrackInfo track = new() { Id = "t9", Title = "Song", Artist = "Artist" };
        SyncedLyrics lineLevel = LineLevel("t9");
        SyncedLyrics laterLineLevel = LineLevel("t9");

        _mockRepo.GetCachedLyricsAsync("t9").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(lineLevel));
        _fallbackProvider.FetchLyricsAsync(track).Returns(fallbackUnavailable
            ? LyricsFetchResult.Unavailable()
            : LyricsFetchResult.Found(laterLineLevel));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(lineLevel);
        await _mockRepo.Received(1).SaveLyricsAsync(lineLevel, cancellationToken: Arg.Any<CancellationToken>());
        await _mockRepo.Received(1).SaveLyricsAsync(
            Arg.Any<SyncedLyrics>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenPrimaryReportsInstrumental_SkipsFallback()
    {
        TrackInfo track = new() { Id = "t10", Title = "Song", Artist = "Artist" };
        SyncedLyrics instrumental = new() { TrackId = "t10", Title = "Song", Artist = "Artist", IsInstrumental = true };

        _mockRepo.GetCachedLyricsAsync("t10").Returns((SyncedLyrics?)null);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(instrumental));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(instrumental);
        await _fallbackProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCachedLineLevelAndUpgradeAllowed_UpgradesToWordTiming()
    {
        TrackInfo track = new() { Id = "t11", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = LineLevel("t11");
        SyncedLyrics wordTimed = WordTimed("t11");

        _mockRepo.GetCachedLyricsAsync("t11").Returns(cached);
        _mockRepo.TryStartWordTimingUpgradeAsync("t11", 3, TimeSpan.FromDays(14), Arg.Any<CancellationToken>())
            .Returns(true);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(LineLevel("t11")));
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(wordTimed));

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(wordTimed);
        await _mockRepo.Received(1).SaveLyricsAsync(wordTimed, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenUpgradeFindsNoWordTiming_KeepsCachedEntryUntouched()
    {
        TrackInfo track = new() { Id = "t12", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = LineLevel("t12");

        _mockRepo.GetCachedLyricsAsync("t12").Returns(cached);
        _mockRepo.TryStartWordTimingUpgradeAsync("t12", 3, TimeSpan.FromDays(14), Arg.Any<CancellationToken>())
            .Returns(true);
        _primaryProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Found(LineLevel("t12")));
        _fallbackProvider.FetchLyricsAsync(track).Returns(LyricsFetchResult.Unavailable());

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _mockRepo.DidNotReceive().SaveLyricsAsync(
            Arg.Any<SyncedLyrics>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
        await _mockRepo.DidNotReceive().MarkNotFoundAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCachedLineLevelAndUpgradeNotDue_DoesNotCallProviders()
    {
        TrackInfo track = new() { Id = "t13", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = LineLevel("t13");

        _mockRepo.GetCachedLyricsAsync("t13").Returns(cached);
        _mockRepo.TryStartWordTimingUpgradeAsync("t13", 3, TimeSpan.FromDays(14), Arg.Any<CancellationToken>())
            .Returns(false);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _primaryProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
        await _fallbackProvider.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCachedHasWordTiming_DoesNotAskForUpgrade()
    {
        TrackInfo track = new() { Id = "t14", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = WordTimed("t14");

        _mockRepo.GetCachedLyricsAsync("t14").Returns(cached);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _mockRepo.DidNotReceive().TryStartWordTimingUpgradeAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenCachedIsInstrumental_DoesNotAskForUpgrade()
    {
        TrackInfo track = new() { Id = "t15", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = new() { TrackId = "t15", Title = "Song", Artist = "Artist", IsInstrumental = true };

        _mockRepo.GetCachedLyricsAsync("t15").Returns(cached);

        SyncedLyrics? result = await _service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _mockRepo.DidNotReceive().TryStartWordTimingUpgradeAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLyricsAsync_WhenUpgradesAreDisabled_DoesNotAskForUpgrade()
    {
        CachedLyricsService service = new(
            _mockRepo,
            new[] { _primaryProvider, _fallbackProvider },
            Options.Create(new LyricsCacheOptions { WordTimingUpgradeMaxAttempts = 0 }),
            NullLogger<CachedLyricsService>.Instance);
        TrackInfo track = new() { Id = "t16", Title = "Song", Artist = "Artist" };
        SyncedLyrics cached = LineLevel("t16");

        _mockRepo.GetCachedLyricsAsync("t16").Returns(cached);

        SyncedLyrics? result = await service.GetLyricsAsync(track);

        result.Should().BeSameAs(cached);
        await _mockRepo.DidNotReceive().TryStartWordTimingUpgradeAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    private static SyncedLyrics LineLevel(string trackId) => new()
    {
        TrackId = trackId,
        Title = "Song",
        Artist = "Artist",
        IsSynced = true,
        Lines = [new(TimeSpan.FromSeconds(1), "alpha bravo")]
    };

    private static SyncedLyrics WordTimed(string trackId) => new()
    {
        TrackId = trackId,
        Title = "Song",
        Artist = "Artist",
        IsSynced = true,
        Lines =
        [
            new(
                TimeSpan.FromSeconds(1),
                "alpha bravo",
                [
                    new(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(300), "alpha "),
                    new(TimeSpan.FromMilliseconds(1300), TimeSpan.FromMilliseconds(400), "bravo")
                ])
        ]
    };
}
