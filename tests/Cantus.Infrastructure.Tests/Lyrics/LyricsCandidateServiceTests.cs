using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public sealed class LyricsCandidateServiceTests
{
    private static readonly TrackInfo Track = new() { Id = "t1", Title = "Song", Artist = "Artist" };

    private readonly ILyricsFetchProvider _primaryProvider;
    private readonly ILyricsFetchProvider _fallbackProvider;
    private readonly LyricsCandidateService _service;

    public LyricsCandidateServiceTests()
    {
        _primaryProvider = Substitute.For<ILyricsFetchProvider>();
        _primaryProvider.ProviderName.Returns("Primary");
        _fallbackProvider = Substitute.For<ILyricsFetchProvider>();
        _fallbackProvider.ProviderName.Returns("Fallback");

        _service = new LyricsCandidateService(
            new[] { _primaryProvider, _fallbackProvider },
            NullLogger<LyricsCandidateService>.Instance);
    }

    private static LyricsCandidate Candidate(string provider, string id) => new(
        provider,
        id,
        "Song",
        "Artist",
        null,
        TimeSpan.FromSeconds(180),
        true,
        false,
        ["line"]);

    [Fact]
    public async Task GetCandidatesAsync_CombinesEveryProviderInChainOrder()
    {
        _primaryProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .Returns(new[] { Candidate("Primary", "p1"), Candidate("Primary", "p2") });
        _fallbackProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .Returns(new[] { Candidate("Fallback", "f1") });

        LyricsCandidateList result = await _service.GetCandidatesAsync(Track);

        result.Candidates.Select(c => c.ProviderCandidateId).Should().Equal("p1", "p2", "f1");
        result.UnavailableProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCandidatesAsync_WhenAProviderThrows_ReturnsTheOthersAndNamesTheFailure()
    {
        _primaryProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("503"));
        _fallbackProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .Returns(new[] { Candidate("Fallback", "f1") });

        LyricsCandidateList result = await _service.GetCandidatesAsync(Track);

        result.Candidates.Select(c => c.ProviderCandidateId).Should().Equal("f1");
        result.UnavailableProviders.Should().Equal("Primary");
    }

    [Fact]
    public async Task GetCandidatesAsync_WhenAProviderTimesOut_TreatsItAsUnavailable()
    {
        // HttpClient reports its own timeout as a cancellation the caller never asked for.
        _primaryProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());
        _fallbackProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LyricsCandidate>());

        LyricsCandidateList result = await _service.GetCandidatesAsync(Track);

        result.Candidates.Should().BeEmpty();
        result.UnavailableProviders.Should().Equal("Primary");
    }

    [Fact]
    public async Task GetCandidatesAsync_WhenEveryProviderFails_ReturnsAnEmptyListNotAnError()
    {
        _primaryProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("down"));
        _fallbackProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bad payload"));

        LyricsCandidateList result = await _service.GetCandidatesAsync(Track);

        result.Candidates.Should().BeEmpty();
        result.UnavailableProviders.Should().Equal("Primary", "Fallback");
    }

    [Fact]
    public async Task GetCandidatesAsync_WhenCallerCancels_Propagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        _primaryProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        _fallbackProvider.GetCandidatesAsync(Track, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LyricsCandidate>());

        Func<Task> act = () => _service.GetCandidatesAsync(Track, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
