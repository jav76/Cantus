using System.Collections.Concurrent;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.CoverArt;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class CoverArtBackfillTests : IDisposable
{
    private const string SESSION_ID = "local-1";
    private const string ART_URL = "https://coverartarchive.org/release-group/9162580e-5df4-32de-80cc-f45a8d8a9b1d/front-500";

    private static readonly TimeSpan WAIT_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ABSENCE_WAIT = TimeSpan.FromMilliseconds(200);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly PlaybackSessionRegistry _registry;
    private readonly StubCoverArtService _coverArt = new();
    private readonly CoverArtOptions _options = new();
    private readonly ConcurrentQueue<CoverArtResolved> _resolved = new();
    private readonly ServiceProvider _services;
    private readonly CoverArtBackfill _backfill;

    public CoverArtBackfillTests()
    {
        ServiceCollection services = new();
        services.AddScoped<ICoverArtService>(_ => _coverArt);
        _services = services.BuildServiceProvider();

        _registry = new PlaybackSessionRegistry(_time);
        _backfill = new CoverArtBackfill(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _registry,
            _time,
            Options.Create(_options),
            NullLogger<CoverArtBackfill>.Instance);
        _backfill.Resolved += (_, resolved) => _resolved.Enqueue(resolved);
    }

    public void Dispose()
    {
        _backfill.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Request_ForATrackShownWithoutACover_RaisesResolvedWithTheCover()
    {
        TrackInfo track = Show("Track A");
        _coverArt.Answer = _ => CoverArtLookup.Found(ART_URL);

        _backfill.Request(track);

        await WaitUntilAsync(() => !_resolved.IsEmpty);
        _resolved.Should().Equal(new CoverArtResolved(track.Id, ART_URL));
    }

    [Fact]
    public async Task Request_WhenTheCatalogueHasNoCover_RaisesNothingAndDoesNotRetry()
    {
        TrackInfo track = Show("Track A");
        _coverArt.Answer = _ => CoverArtLookup.NotFound();

        _backfill.Request(track);

        await WaitUntilAsync(() => _coverArt.Calls.Count == 1);
        await AdvancePastEveryRetryAsync();
        _coverArt.Calls.Should().ContainSingle();
        _resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_ForATrackNoSessionIsShowing_NeverAsksTheCatalogue()
    {
        TrackInfo shown = Show("Track A");
        TrackInfo skippedPast = Track("Track B");
        _coverArt.Answer = _ => CoverArtLookup.Found(ART_URL);

        _backfill.Request(skippedPast);
        _backfill.Request(shown);

        await WaitUntilAsync(() => !_resolved.IsEmpty);
        _coverArt.Calls.Should().Equal(shown.Id);
    }

    [Fact]
    public async Task Request_ForATrackThatGotACoverMeanwhile_NeverAsksTheCatalogue()
    {
        TrackInfo track = Track("Track A") with { AlbumArtUrl = "https://i.scdn.co/image/abc" };
        _registry.UpdateUserPlayback(SESSION_ID, "Desktop", new PlaybackState { CurrentTrack = track });

        _backfill.Request(track);

        await Task.Delay(ABSENCE_WAIT);
        _coverArt.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WhileTheSameTrackIsBeingLookedUp_AsksOnce()
    {
        TrackInfo track = Show("Track A");
        TaskCompletionSource<CoverArtLookup> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _coverArt.AnswerAsync = _ => answer.Task;

        _backfill.Request(track);
        await WaitUntilAsync(() => _coverArt.Calls.Count == 1);
        _backfill.Request(track);
        answer.SetResult(CoverArtLookup.Found(ART_URL));

        await WaitUntilAsync(() => !_resolved.IsEmpty);
        await Task.Delay(ABSENCE_WAIT);
        _coverArt.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Request_OnceALookupFinished_CanBeAskedAgain()
    {
        TrackInfo track = Show("Track A");
        _coverArt.Answer = _ => CoverArtLookup.NotFound();

        _backfill.Request(track);
        await WaitUntilAsync(() => _coverArt.Calls.Count == 1);
        await WaitUntilAsync(() =>
        {
            _backfill.Request(track);
            return _coverArt.Calls.Count >= 2;
        });
    }

    [Fact]
    public async Task Request_WhenTheCatalogueCannotBeReached_TriesAgainAfterADelay()
    {
        TrackInfo track = Show("Track A");
        _coverArt.Answer = attempt => attempt == 1 ? CoverArtLookup.Unresolved() : CoverArtLookup.Found(ART_URL);

        _backfill.Request(track);

        await WaitUntilAsync(() => _coverArt.Calls.Count == 1);
        await Task.Delay(ABSENCE_WAIT);
        _coverArt.Calls.Should().ContainSingle("the retry waits for its delay");

        await WaitUntilAsync(() =>
        {
            _time.Advance(CoverArtBackfill.RetryDelays[0]);
            return !_resolved.IsEmpty;
        });
        _coverArt.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task Request_WhenTheCatalogueStaysUnreachable_GivesUpAfterTheLastRetry()
    {
        TrackInfo track = Show("Track A");
        _coverArt.Answer = _ => CoverArtLookup.Unresolved();
        int attempts = CoverArtBackfill.RetryDelays.Count + 1;

        _backfill.Request(track);

        await WaitUntilAsync(() =>
        {
            _time.Advance(CoverArtBackfill.RetryDelays[^1]);
            return _coverArt.Calls.Count >= attempts;
        });
        await AdvancePastEveryRetryAsync();
        _coverArt.Calls.Should().HaveCount(attempts);
        _resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WhenTheSessionMovesOnBeforeARetry_StopsAsking()
    {
        TrackInfo track = Show("Track A");
        _coverArt.Answer = _ => CoverArtLookup.Unresolved();

        _backfill.Request(track);
        await WaitUntilAsync(() => _coverArt.Calls.Count == 1);
        Show("Track B");

        await AdvancePastEveryRetryAsync();
        _coverArt.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Request_WhenALookupThrows_KeepsWorkingForTheNextTrack()
    {
        TrackInfo failing = Show("Track A", "local-a");
        TrackInfo next = Show("Track B", "local-b");
        _coverArt.Answer = attempt => attempt == 1
            ? throw new InvalidOperationException("boom")
            : CoverArtLookup.Found(ART_URL);

        _backfill.Request(failing);
        _backfill.Request(next);

        await WaitUntilAsync(() => !_resolved.IsEmpty);
        _resolved.Should().Equal(new CoverArtResolved(next.Id, ART_URL));
    }

    [Fact]
    public async Task Request_WhenTheQueueIsFull_DropsTheOldestWaitingTrackAndLetsItBeAskedAgain()
    {
        TrackInfo inFlight = Show("In flight", "local-in-flight");
        TaskCompletionSource<CoverArtLookup> firstAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _coverArt.AnswerAsync = attempt => attempt == 1 ? firstAnswer.Task : Task.FromResult(CoverArtLookup.NotFound());
        _backfill.Request(inFlight);
        await WaitUntilAsync(() => _coverArt.Calls.Count == 1);

        List<TrackInfo> waiting = Enumerable.Range(0, CoverArtBackfill.QUEUE_CAPACITY + 1)
            .Select(i => Show($"Waiting {i}", $"local-{i}"))
            .ToList();
        waiting.ForEach(_backfill.Request);
        firstAnswer.SetResult(CoverArtLookup.NotFound());

        await WaitUntilAsync(() => _coverArt.Calls.Count == 1 + CoverArtBackfill.QUEUE_CAPACITY);
        _coverArt.Calls.Should().NotContain(waiting[0].Id, "the oldest waiting track made room for the newest");
        _coverArt.Calls.Should().Contain(waiting[^1].Id);

        _backfill.Request(waiting[0]);
        await WaitUntilAsync(() => _coverArt.Calls.Contains(waiting[0].Id));
    }

    [Fact]
    public async Task Request_WhenTurnedOff_DoesNothing()
    {
        _options.Enabled = false;
        TrackInfo track = Show("Track A");

        _backfill.IsEnabled.Should().BeFalse();
        _backfill.Request(track);

        await Task.Delay(ABSENCE_WAIT);
        _coverArt.Calls.Should().BeEmpty();
    }

    private async Task AdvancePastEveryRetryAsync()
    {
        foreach (TimeSpan delay in CoverArtBackfill.RetryDelays)
        {
            _time.Advance(delay);
            await Task.Delay(ABSENCE_WAIT / 2);
        }
    }

    /// <summary>Puts a track with no cover on a Session's screen, as the applier does before a request.</summary>
    private TrackInfo Show(string title, string sessionId = SESSION_ID)
    {
        TrackInfo track = Track(title);
        _registry.UpdateUserPlayback(sessionId, "Desktop", new PlaybackState { CurrentTrack = track });
        return track;
    }

    private static TrackInfo Track(string title) => new()
    {
        Id = "local:" + title,
        Title = title,
        Artist = "Artist",
        Duration = TimeSpan.FromMinutes(3)
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(WAIT_TIMEOUT);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubCoverArtService : ICoverArtService
    {
        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyCollection<string> Calls => _calls;

        /// <summary>Answers by the number of the call, starting at 1.</summary>
        public Func<int, CoverArtLookup> Answer { get; set; } = _ => CoverArtLookup.Unresolved();

        public Func<int, Task<CoverArtLookup>>? AnswerAsync { get; set; }

        public Task<CoverArtLookup> GetCachedAsync(TrackInfo track, CancellationToken cancellationToken = default) =>
            Task.FromResult(CoverArtLookup.Unresolved());

        public Task<CoverArtLookup> ResolveAsync(TrackInfo track, CancellationToken cancellationToken = default)
        {
            _calls.Enqueue(track.Id);
            int attempt = _calls.Count;
            return AnswerAsync is not null ? AnswerAsync(attempt) : Task.FromResult(Answer(attempt));
        }
    }
}
