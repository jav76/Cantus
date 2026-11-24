using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public sealed class LyricsOverrideServiceTests
{
    private static readonly TrackInfo Track = new()
    {
        Id = "track-1",
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Duration = TimeSpan.FromSeconds(295)
    };

    private const int NEGATIVE_CACHE_DAYS = 30;

    private readonly ILyricsCacheRepository _repository = Substitute.For<ILyricsCacheRepository>();
    private readonly ILyricsFetchProvider _lrclib = Substitute.For<ILyricsFetchProvider>();
    private readonly ILyricsFetchProvider _netease = Substitute.For<ILyricsFetchProvider>();
    private readonly LyricsOverrideService _service;

    public LyricsOverrideServiceTests()
    {
        _lrclib.ProviderName.Returns("LRCLIB");
        _netease.ProviderName.Returns("NetEase");
        _netease.SupportsWordTiming.Returns(true);
        _lrclib.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.NotFound());
        _netease.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.NotFound());

        ILyricsFetchProvider[] providers = { _lrclib, _netease };
        CachedLyricsService automaticLyrics = new(
            _repository,
            providers,
            Options.Create(new LyricsCacheOptions { NegativeCacheDays = NEGATIVE_CACHE_DAYS }),
            NullLogger<CachedLyricsService>.Instance);

        _service = new LyricsOverrideService(
            _repository,
            providers,
            automaticLyrics,
            NullLogger<LyricsOverrideService>.Instance);
    }

    private static SyncedLyrics Lyrics(string text) => new()
    {
        TrackId = Track.Id,
        Title = Track.Title,
        Artist = Track.Artist,
        IsSynced = true,
        Lines = [new LyricLine(TimeSpan.Zero, text)]
    };

    private static SyncedLyrics WordTimedLyrics(string text) => new()
    {
        TrackId = Track.Id,
        Title = Track.Title,
        Artist = Track.Artist,
        IsSynced = true,
        Lines =
        [
            new LyricLine(
                TimeSpan.Zero,
                text,
                [new LyricSyllable(TimeSpan.Zero, TimeSpan.FromMilliseconds(500), text)])
        ]
    };

    [Fact]
    public async Task UseCandidate_StoresTheCandidateAsTheTracksOverride()
    {
        SyncedLyrics chosen = Lyrics("Chosen");
        _netease.GetCandidateLyricsAsync(Track, "111", Arg.Any<CancellationToken>()).Returns(chosen);

        LyricsOverrideOutcome outcome = await _service.UseCandidateAsync(Track, "netease", "111");

        outcome.Status.Should().Be(LyricsOverrideStatus.Applied);
        outcome.Lyrics.Should().BeSameAs(chosen);
        await _repository.Received(1).SaveLyricsOverrideAsync(
            Arg.Is<LyricsOverride>(o =>
                o.TrackId == Track.Id
                && o.Kind == LyricsOverrideKind.Candidate
                && o.Provider == "NetEase"
                && o.ProviderCandidateId == "111"
                && o.Lyrics == chosen),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseCandidate_FromAProviderThatIsNotEnabled_ReportsUnknownProvider()
    {
        LyricsOverrideOutcome outcome = await _service.UseCandidateAsync(Track, "Musixmatch", "1");

        outcome.Status.Should().Be(LyricsOverrideStatus.UnknownProvider);
        await _repository.DidNotReceive().SaveLyricsOverrideAsync(
            Arg.Any<LyricsOverride>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseCandidate_ThatTheProviderDoesNotHave_ReportsCandidateNotFound()
    {
        _lrclib.GetCandidateLyricsAsync(Track, "999", Arg.Any<CancellationToken>()).Returns((SyncedLyrics?)null);

        LyricsOverrideOutcome outcome = await _service.UseCandidateAsync(Track, "LRCLIB", "999");

        outcome.Status.Should().Be(LyricsOverrideStatus.CandidateNotFound);
        await _repository.DidNotReceive().SaveLyricsOverrideAsync(
            Arg.Any<LyricsOverride>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseCandidate_WhenTheProviderFails_ReportsProviderUnavailableAndKeepsTheCurrentLyrics()
    {
        _lrclib.GetCandidateLyricsAsync(Track, "501", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("503"));

        LyricsOverrideOutcome outcome = await _service.UseCandidateAsync(Track, "LRCLIB", "501");

        outcome.Status.Should().Be(LyricsOverrideStatus.ProviderUnavailable);
        await _repository.DidNotReceive().SaveLyricsOverrideAsync(
            Arg.Any<LyricsOverride>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkNoLyrics_StoresANoLyricsOverride()
    {
        await _service.MarkNoLyricsAsync(Track);

        await _repository.Received(1).SaveLyricsOverrideAsync(
            Arg.Is<LyricsOverride>(o => o.TrackId == Track.Id && o.Kind == LyricsOverrideKind.NoLyrics),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refetch_WhenAProviderFindsLyrics_ReplacesTheCachedMatchAndDropsTheOverride()
    {
        SyncedLyrics refetched = Lyrics("Fresh");
        List<string> calls = new();
        _netease.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Found(refetched));
        _repository.SaveLyricsAsync(refetched, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => calls.Add("save-match"));
        _repository.RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => calls.Add("remove-override"));

        LyricsOverrideOutcome outcome = await _service.RefetchAsync(Track);

        outcome.Status.Should().Be(LyricsOverrideStatus.Applied);
        outcome.Lyrics.Should().BeSameAs(refetched);
        calls.Should().Equal("save-match", "remove-override");
        await _repository.DidNotReceive().GetLyricsOverrideAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().GetCachedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().IsMarkedNotFoundAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refetch_WhenEveryProviderHasNoLyrics_MarksTheTrackNotFoundAndDropsTheOverride()
    {
        LyricsOverrideOutcome outcome = await _service.RefetchAsync(Track);

        outcome.Status.Should().Be(LyricsOverrideStatus.Applied);
        outcome.Lyrics.Should().BeNull();
        await _repository.Received(1).MarkNotFoundAsync(
            Track.Id,
            Track.Title,
            Track.Artist,
            Arg.Any<string>(),
            Arg.Any<int>(),
            TimeSpan.FromDays(NEGATIVE_CACHE_DAYS),
            Arg.Any<CancellationToken>());
        await _repository.Received(1).RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refetch_WhenNoProviderHasATrackWhoseTitleMarksNoVocals_AppliesInstrumentalLyrics()
    {
        TrackInfo instrumental = Track with { Id = "track-inst", Title = "Miss America (Instrumental)" };
        _lrclib.FetchLyricsAsync(instrumental, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.NotFound());
        _netease.FetchLyricsAsync(instrumental, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.NotFound());

        LyricsOverrideOutcome outcome = await _service.RefetchAsync(instrumental);

        outcome.Status.Should().Be(LyricsOverrideStatus.Applied);
        outcome.Lyrics.Should().NotBeNull();
        outcome.Lyrics!.TrackId.Should().Be("track-inst");
        outcome.Lyrics.IsInstrumental.Should().BeTrue();
    }

    [Fact]
    public async Task Refetch_WhenAProviderIsUnavailable_ReportsProviderUnavailableAndChangesNothing()
    {
        _lrclib.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Unavailable());

        LyricsOverrideOutcome outcome = await _service.RefetchAsync(Track);

        outcome.Status.Should().Be(LyricsOverrideStatus.ProviderUnavailable);
        outcome.Lyrics.Should().BeNull();
        await _repository.DidNotReceive().RemoveLyricsOverrideAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveLyricsAsync(
            Arg.Any<SyncedLyrics>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().MarkNotFoundAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_RemovesTheOverrideAndReturnsTheAutomaticMatch()
    {
        SyncedLyrics automatic = Lyrics("Automatic");
        List<string> calls = new();
        _repository.RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => calls.Add("remove-override"));
        _repository.GetCachedLyricsAsync(Track.Id, Arg.Any<CancellationToken>())
            .Returns(automatic)
            .AndDoes(_ => calls.Add("match"));

        SyncedLyrics? result = await _service.ClearAsync(Track);

        result.Should().BeSameAs(automatic);
        calls.Should().Equal("remove-override", "match");
        await _lrclib.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refetch_WhenALaterProviderHasWordTiming_PrefersItOverTheFirstLineLevelMatch()
    {
        SyncedLyrics lineLevel = Lyrics("Line level");
        SyncedLyrics wordTimed = WordTimedLyrics("Word timed");
        _lrclib.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Found(lineLevel));
        _netease.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Found(wordTimed));

        LyricsOverrideOutcome outcome = await _service.RefetchAsync(Track);

        outcome.Status.Should().Be(LyricsOverrideStatus.Applied);
        outcome.Lyrics.Should().BeSameAs(wordTimed);
        await _repository.Received(1).SaveLyricsAsync(wordTimed, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveLyricsAsync(lineLevel, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_WhenTheCachedMatchIsLineLevel_ReturnsToTheWordTimingUpgradePolicy()
    {
        SyncedLyrics wordTimed = WordTimedLyrics("Word timed");
        _repository.GetCachedLyricsAsync(Track.Id, Arg.Any<CancellationToken>()).Returns(Lyrics("Line level"));
        _repository.TryStartWordTimingUpgradeAsync(
                Track.Id,
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _netease.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Found(wordTimed));

        SyncedLyrics? result = await _service.ClearAsync(Track);

        result.Should().BeSameAs(wordTimed);
        await _repository.Received(1).RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveLyricsAsync(wordTimed, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _lrclib.DidNotReceive().FetchLyricsAsync(Arg.Any<TrackInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_WhenNothingIsCached_PrefersWordTimingFromTheProviderChain()
    {
        SyncedLyrics wordTimed = WordTimedLyrics("Word timed");
        _lrclib.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Found(Lyrics("Line level")));
        _netease.FetchLyricsAsync(Track, Arg.Any<CancellationToken>()).Returns(LyricsFetchResult.Found(wordTimed));

        SyncedLyrics? result = await _service.ClearAsync(Track);

        result.Should().BeSameAs(wordTimed);
        await _repository.Received(1).SaveLyricsAsync(wordTimed, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }
}
