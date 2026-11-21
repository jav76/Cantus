using Cantus.Core.Models;
using Cantus.Server.BackgroundServices;
using FluentAssertions;

namespace Cantus.Server.Tests.BackgroundServices;

/// <summary>
/// One case per cadence in docs/architecture/adaptive-polling.md, using the default
/// <see cref="PlaybackPollerOptions"/> so the numbers match the documented table.
/// </summary>
public sealed class PollingCadencePolicyTests
{
    private static readonly PollingCadencePolicy Policy = new(new PlaybackPollerOptions());

    private static readonly TimeSpan TrackLength = TimeSpan.FromMinutes(4);

    private static PlaybackState Playing(TimeSpan remaining, TimeSpan? duration = null)
    {
        TimeSpan length = duration ?? TrackLength;
        return new PlaybackState
        {
            IsPlaying = true,
            CurrentTrack = new TrackInfo { Id = "t-1", Title = "Song", Artist = "Artist", Duration = length },
            Progress = length - remaining
        };
    }

    private static PlaybackState Paused() => new()
    {
        IsPlaying = false,
        CurrentTrack = new TrackInfo { Id = "t-1", Title = "Song", Artist = "Artist", Duration = TrackLength },
        Progress = TimeSpan.FromMinutes(1)
    };

    public static TheoryData<string, PlaybackState?, bool, TimeSpan, int> UserCadences => new()
    {
        // Playing: horizon acceleration toward the end of the track.
        { "active playing baseline", Playing(TimeSpan.FromSeconds(60)), true, TimeSpan.Zero, 4000 },
        { "just over the approaching threshold", Playing(TimeSpan.FromMilliseconds(15_001)), true, TimeSpan.Zero, 4000 },
        { "approaching end at the threshold", Playing(TimeSpan.FromMilliseconds(15_000)), true, TimeSpan.Zero, 2500 },
        { "approaching end", Playing(TimeSpan.FromSeconds(10)), true, TimeSpan.Zero, 2500 },
        { "imminent end at the threshold", Playing(TimeSpan.FromMilliseconds(5_000)), true, TimeSpan.Zero, 1200 },
        { "imminent end", Playing(TimeSpan.FromSeconds(2)), true, TimeSpan.Zero, 1200 },
        { "progress past the end", Playing(TimeSpan.FromSeconds(-3)), true, TimeSpan.Zero, 1200 },
        { "playing a track of unknown length", Playing(TimeSpan.Zero, TimeSpan.Zero), true, TimeSpan.Zero, 4000 },
        { "playing with no track", new PlaybackState { IsPlaying = true }, true, TimeSpan.Zero, 4000 },

        // Paused: graduated backoff by how long playback has been paused.
        { "paused initial", Paused(), true, TimeSpan.FromSeconds(30), 5000 },
        { "paused at one minute", Paused(), true, TimeSpan.FromMinutes(1), 5000 },
        { "paused extended", Paused(), true, TimeSpan.FromMinutes(3), 15000 },
        { "paused at five minutes", Paused(), true, TimeSpan.FromMinutes(5), 15000 },
        { "paused deep", Paused(), true, TimeSpan.FromMinutes(6), 30000 },

        // Idle: no playback at all, graduated backoff by idle time.
        { "idle initial", null, true, TimeSpan.FromSeconds(30), 10000 },
        { "idle at two minutes", null, true, TimeSpan.FromMinutes(2), 10000 },
        { "idle extended", null, true, TimeSpan.FromMinutes(5), 30000 },
        { "idle at ten minutes", null, true, TimeSpan.FromMinutes(10), 30000 },
        { "idle deep", null, true, TimeSpan.FromMinutes(11), 60000 },

        // Background throttled: every viewer is hidden.
        { "background while playing", Playing(TimeSpan.FromSeconds(60)), false, TimeSpan.Zero, 20000 },
        { "background near the end of a track", Playing(TimeSpan.FromSeconds(2)), false, TimeSpan.Zero, 20000 },
        { "background while paused", Paused(), false, TimeSpan.FromSeconds(30), 30000 },
        { "background while idle", null, false, TimeSpan.FromSeconds(30), 60000 },
    };

    [Theory]
    [MemberData(nameof(UserCadences))]
    public void GetUserPollIntervalMs_ReturnsTheDocumentedCadence(
        string cadence,
        PlaybackState? playback,
        bool isVisible,
        TimeSpan timeInState,
        int expectedMs)
    {
        Policy.GetUserPollIntervalMs(playback, isVisible, timeInState)
            .Should().Be(expectedMs, cadence);
    }

    public static TheoryData<string, int, int, int> BackgroundFloors => new()
    {
        // Hidden paused and idle owners poll at the slower of their deep cadence and the background cadence.
        { "background slower than paused deep", 45000, 30000, 45000 },
        { "paused deep slower than background", 10000, 30000, 30000 },
    };

    [Theory]
    [MemberData(nameof(BackgroundFloors))]
    public void GetUserPollIntervalMs_WhenHiddenAndPaused_UsesTheSlowerOfDeepAndBackground(
        string cadence,
        int backgroundMs,
        int pausedDeepMs,
        int expectedMs)
    {
        PollingCadencePolicy policy = new(new PlaybackPollerOptions
        {
            BackgroundPollIntervalMs = backgroundMs,
            PausedDeepPollIntervalMs = pausedDeepMs
        });

        policy.GetUserPollIntervalMs(Paused(), isVisible: false, TimeSpan.Zero).Should().Be(expectedMs, cadence);
    }

    [Fact]
    public void PollErrorIntervalMs_RetriesAtThePausedCadence()
    {
        Policy.PollErrorIntervalMs.Should().Be(5000);
    }

    [Fact]
    public void NoActiveUsersDelayMs_RechecksAtTheIdleCadence()
    {
        Policy.NoActiveUsersDelayMs.Should().Be(10000);
    }

    [Fact]
    public void NoConnectedClientsDelayMs_SleepsWithoutPollingSpotify()
    {
        PollingCadencePolicy.NO_CONNECTED_CLIENTS_DELAY_MS.Should().Be(1000);
    }

    public static TheoryData<string, double, int, int> RateLimitCadences => new()
    {
        { "long cooldown is capped", 120_000, 10000, 10000 },
        { "cooldown inside the cap", 4_500, 4500, 4500 },
        { "short cooldown waits at least a second", 200, 1000, 200 },
        { "cooldown at the cap", 10_000, 10000, 10000 },
    };

    [Theory]
    [MemberData(nameof(RateLimitCadences))]
    public void RateLimited_WaitsForTheCooldownWithinBounds(
        string cadence,
        double remainingMs,
        int expectedLoopDelayMs,
        int expectedReportedIntervalMs)
    {
        TimeSpan remaining = TimeSpan.FromMilliseconds(remainingMs);

        Policy.GetRateLimitedLoopDelayMs(remaining).Should().Be(expectedLoopDelayMs, cadence);
        Policy.GetRateLimitedReportedIntervalMs(remaining).Should().Be(expectedReportedIntervalMs, cadence);
    }

    public static TheoryData<string, double?, int> LoopDelays => new()
    {
        { "nothing scheduled falls back to the active cadence", null, 4000 },
        { "next poll inside the bounds", 2_500, 2500 },
        { "overdue poll waits the minimum", -1_000, 500 },
        { "very short wait is raised to the minimum", 100, 500 },
        { "long wait is capped so new viewers are noticed", 60_000, 10000 },
    };

    [Theory]
    [MemberData(nameof(LoopDelays))]
    public void GetLoopDelayMs_WaitsUntilTheEarliestScheduledPollWithinBounds(
        string cadence,
        double? earliestInMs,
        int expectedMs)
    {
        DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset? earliest = earliestInMs is null ? null : now.AddMilliseconds(earliestInMs.Value);

        Policy.GetLoopDelayMs(earliest, now).Should().Be(expectedMs, cadence);
    }
}
