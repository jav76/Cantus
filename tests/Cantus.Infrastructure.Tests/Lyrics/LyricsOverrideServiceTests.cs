using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

    private readonly ILyricsCacheRepository _repository = Substitute.For<ILyricsCacheRepository>();
    private readonly ILyricsFetchProvider _lrclib = Substitute.For<ILyricsFetchProvider>();
    private readonly ILyricsFetchProvider _netease = Substitute.For<ILyricsFetchProvider>();
    private readonly ILyricsProvider _automaticLyrics = Substitute.For<ILyricsProvider>();
    private readonly LyricsOverrideService _service;

    public LyricsOverrideServiceTests()
    {
        _lrclib.ProviderName.Returns("LRCLIB");
        _netease.ProviderName.Returns("NetEase");

        _service = new LyricsOverrideService(
            _repository,
            new[] { _lrclib, _netease },
            _automaticLyrics,
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
    public async Task Refetch_DropsTheOverrideAndCachedMatchBeforeMatchingAgain()
    {
        SyncedLyrics refetched = Lyrics("Fresh");
        List<string> calls = new();
        _repository.RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => calls.Add("remove-override"));
        _repository.RemoveCachedLyricsAsync(Track.Id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => calls.Add("remove-cache"));
        _automaticLyrics.GetLyricsAsync(Track, Arg.Any<CancellationToken>())
            .Returns(refetched)
            .AndDoes(_ => calls.Add("match"));

        SyncedLyrics? result = await _service.RefetchAsync(Track);

        result.Should().BeSameAs(refetched);
        calls.Should().Equal("remove-override", "remove-cache", "match");
    }

    [Fact]
    public async Task Clear_RemovesTheOverrideAndReturnsTheAutomaticMatch()
    {
        SyncedLyrics automatic = Lyrics("Automatic");
        List<string> calls = new();
        _repository.RemoveLyricsOverrideAsync(Track.Id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => calls.Add("remove-override"));
        _automaticLyrics.GetLyricsAsync(Track, Arg.Any<CancellationToken>())
            .Returns(automatic)
            .AndDoes(_ => calls.Add("match"));

        SyncedLyrics? result = await _service.ClearAsync(Track);

        result.Should().BeSameAs(automatic);
        calls.Should().Equal("remove-override", "match");
        await _repository.DidNotReceive().RemoveCachedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
