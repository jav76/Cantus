using System.Collections.Concurrent;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Sessions;
using Cantus.Server.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class LocalPlaybackIngestTests : IDisposable
{
    private const string CONNECTION_A = "conn-a";
    private const string CONNECTION_B = "conn-b";
    private const string CONNECTION_C = "conn-c";
    private const long DURATION_MS = 233_000;
    private const int GRACE_SECONDS = 20;

    private static readonly TimeSpan WAIT_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ABSENCE_WAIT = TimeSpan.FromMilliseconds(200);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingApplier _applier = new();
    private readonly PlaybackSessionRegistry _registry;
    private readonly LocalSessionOptions _options = new() { Enabled = true, PublisherGraceSeconds = GRACE_SECONDS };
    private readonly Mock<ILocalSessionService> _localSessions = new();
    private readonly ServiceProvider _services;
    private readonly LocalPlaybackIngest _ingest;
    private readonly UserSession _session = LocalSession("local-1");

    public LocalPlaybackIngestTests()
    {
        ServiceCollection services = new();
        services.AddScoped(_ => Mock.Of<ILyricsProvider>());
        services.AddScoped(_ => Mock.Of<ILyricsCacheRepository>());
        services.AddScoped(_ => _localSessions.Object);
        _services = services.BuildServiceProvider();

        _registry = new PlaybackSessionRegistry(_time);
        foreach (string connectionId in new[] { CONNECTION_A, CONNECTION_B, CONNECTION_C })
        {
            _registry.RegisterConnection(connectionId, _session.Id);
        }

        _ingest = new LocalPlaybackIngest(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _applier,
            _registry,
            _time,
            Options.Create(_options),
            NullLogger<LocalPlaybackIngest>.Instance);
    }

    public void Dispose()
    {
        _ingest.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void Submit_FromSpotifySession_IsRefused()
    {
        UserSession spotify = new() { Id = "spotify-1", Kind = SessionKind.Spotify, DisplayName = "Owner" };

        _ingest.Submit(CONNECTION_A, spotify, Update(1)).Should().Be(LocalPublishResult.NotLocalSession);
    }

    [Fact]
    public async Task Submit_WhenLocalSessionsAreDisabled_IsRefusedForExistingSessions()
    {
        _options.Enabled = false;

        _ingest.Submit(CONNECTION_A, _session, Update(1)).Should().Be(LocalPublishResult.NotLocalSession);

        await Task.Delay(ABSENCE_WAIT);
        _applier.Updates.Should().BeEmpty();
    }

    public static TheoryData<string> InvalidUpdateCases() => new()
    {
        "null update",
        "no player",
        "blank player id",
        "control-only player id",
        "long player id",
        "long player name",
        "long device name",
        "blank title",
        "blank artist",
        "long title",
        "long artist",
        "long album",
        "too short",
        "too long",
    };

    [Theory]
    [MemberData(nameof(InvalidUpdateCases))]
    public async Task Submit_WithInvalidUpdate_ReturnsInvalidAndAppliesNothing(string invalidCase)
    {
        string tooLongPlayerField = new('p', LocalPlaybackIngest.MAX_PLAYER_FIELD_LENGTH + 1);
        string tooLongTrackField = new('t', LocalPlaybackIngest.MAX_TRACK_FIELD_LENGTH + 1);
        LocalPlaybackUpdateDto valid = Update(1);
        LocalTrackDto track = valid.Track!;
        LocalPlaybackUpdateDto? update = invalidCase switch
        {
            "null update" => null,
            "no player" => valid with { Player = null },
            "blank player id" => valid with { Player = new LocalPlayerDto { Id = "  ", Name = "Spotify" } },
            "control-only player id" => valid with { Player = new LocalPlayerDto { Id = "\r\n\u0000", Name = "Spotify" } },
            "long player id" => valid with { Player = new LocalPlayerDto { Id = tooLongPlayerField } },
            "long player name" => valid with { Player = new LocalPlayerDto { Id = "spotify", Name = tooLongPlayerField } },
            "long device name" => valid with { DeviceName = tooLongPlayerField },
            "blank title" => valid with { Track = track with { Title = " " } },
            "blank artist" => valid with { Track = track with { Artist = null } },
            "long title" => valid with { Track = track with { Title = tooLongTrackField } },
            "long artist" => valid with { Track = track with { Artist = tooLongTrackField } },
            "long album" => valid with { Track = track with { Album = tooLongTrackField } },
            "too short" => valid with { Track = track with { DurationMs = LocalPlaybackIngest.MIN_DURATION_MS - 1 } },
            "too long" => valid with { Track = track with { DurationMs = LocalPlaybackIngest.MAX_DURATION_MS + 1 } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };

        _ingest.Submit(CONNECTION_A, _session, update).Should().Be(LocalPublishResult.Invalid);

        await Task.Delay(ABSENCE_WAIT);
        _applier.Updates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LocalPlaybackIngest.MIN_DURATION_MS)]
    [InlineData(LocalPlaybackIngest.MAX_DURATION_MS)]
    public async Task Submit_WithDurationAtTheLimits_IsAccepted(long durationMs)
    {
        LocalPlaybackUpdateDto update = Update(1) with { Track = Update(1).Track! with { DurationMs = durationMs } };

        _ingest.Submit(CONNECTION_A, _session, update).Should().Be(LocalPublishResult.Accepted);

        await WaitForUpdatesAsync(1);
    }

    [Fact]
    public async Task Submit_BuildsALocalStateWithTheDerivedTrackKey()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);

        PlaybackUpdate applied = (await WaitForUpdatesAsync(1))[0];
        applied.SessionId.Should().Be(_session.Id);
        applied.DisplayName.Should().Be(_session.DisplayName);
        applied.AllowLyricsFetch.Should().BeTrue();
        applied.ClaimedSpotifyTrackId.Should().Be("2mlNgAeIBnL78ZriXgrRHz");
        PlaybackState state = applied.State;
        state.Source.Should().Be(PlaybackSource.Local);
        state.IsPlaying.Should().BeTrue();
        state.DeviceName.Should().Be("Spotify");
        state.Progress.Should().Be(TimeSpan.FromMilliseconds(98_774));
        state.Capabilities.Should().Be(new PlaybackCapabilities
        {
            CanPlay = true,
            CanPause = true,
            CanGoNext = true,
            CanGoPrevious = false,
            CanSeek = true
        });
        state.CurrentTrack!.Id.Should().Be(
            TrackIdentity.DeriveLocalKey("Glimpse of Us", "Joji", TimeSpan.FromMilliseconds(DURATION_MS)));
        state.CurrentTrack.Title.Should().Be("Glimpse of Us");
        state.CurrentTrack.Album.Should().Be("SMITHEREENS");
        state.CurrentTrack.AlbumArtUrl.Should().Be("https://i.scdn.co/image/abc");
        state.CurrentTrack.Duration.Should().Be(TimeSpan.FromMilliseconds(DURATION_MS));
    }

    [Fact]
    public async Task Submit_WithControlCharactersInText_ReplacesThemSoTheyCannotForgeLogLines()
    {
        LocalPlaybackUpdateDto update = Update(1) with
        {
            DeviceName = "Spot\tify",
            Track = Update(1).Track! with
            {
                Title = "Glimpse\r\n2026-11-23 12:00:00,000 INFO fake",
                Artist = "Jo\u2028ji ",
                Album = "\u0007"
            }
        };

        _ingest.Submit(CONNECTION_A, _session, update).Should().Be(LocalPublishResult.Accepted);

        PlaybackState state = (await WaitForUpdatesAsync(1))[0].State;
        state.DeviceName.Should().Be("Spot ify");
        state.CurrentTrack!.Title.Should().Be("Glimpse 2026-11-23 12:00:00,000 INFO fake");
        state.CurrentTrack.Artist.Should().Be("Jo ji");
        state.CurrentTrack.Album.Should().BeNull();
        state.CurrentTrack.Id.Should().Be(TrackIdentity.DeriveLocalKey(
            state.CurrentTrack.Title,
            state.CurrentTrack.Artist,
            TimeSpan.FromMilliseconds(DURATION_MS)));
    }

    public static TheoryData<long, double, string?, string?, long, double, string?, string?> ClampCases() => new()
    {
        { -5_000, 1.0, null, null, 0, 1.0, null, null },
        { DURATION_MS + 10_000, 10.0, "file:///home/me/cover.png", "not-an-id", DURATION_MS, LocalPlaybackIngest.MAX_RATE, null, null },
        { 1_000, 0.0, "javascript:alert(1)", "2mlNgAeIBnL78ZriXgrRHz", 1_000, LocalPlaybackIngest.MIN_RATE, null, "2mlNgAeIBnL78ZriXgrRHz" },
        { 1_000, double.NaN, "http://" + new string('a', LocalPlaybackIngest.MAX_ALBUM_ART_URL_LENGTH), null, 1_000, 1.0, null, null },
        { 1_000, 1.5, "http://art.example/cover.jpg", null, 1_000, 1.5, "http://art.example/cover.jpg", null },
    };

    [Theory]
    [MemberData(nameof(ClampCases))]
    public async Task Submit_ClampsPositionAndRateAndDropsBadArtAndSpotifyIds(
        long positionMs,
        double rate,
        string? artUrl,
        string? spotifyTrackId,
        long expectedPositionMs,
        double expectedRate,
        string? expectedArtUrl,
        string? expectedSpotifyTrackId)
    {
        LocalPlaybackUpdateDto update = Update(1) with
        {
            PositionMs = positionMs,
            Rate = rate,
            Track = Update(1).Track! with { AlbumArtUrl = artUrl, SpotifyTrackId = spotifyTrackId }
        };

        _ingest.Submit(CONNECTION_A, _session, update).Should().Be(LocalPublishResult.Accepted);

        PlaybackUpdate applied = (await WaitForUpdatesAsync(1))[0];
        applied.State.Progress.Should().Be(TimeSpan.FromMilliseconds(expectedPositionMs));
        applied.State.PlaybackRate.Should().Be(expectedRate);
        applied.State.CurrentTrack!.AlbumArtUrl.Should().Be(expectedArtUrl);
        applied.ClaimedSpotifyTrackId.Should().Be(expectedSpotifyTrackId);
    }

    [Fact]
    public async Task Submit_WithNoTrackOrCapabilities_AppliesAnIdleStateWithNoControls()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1) with { Track = null, Capabilities = null })
            .Should().Be(LocalPublishResult.Accepted);

        PlaybackState state = (await WaitForUpdatesAsync(1))[0].State;
        state.CurrentTrack.Should().BeNull();
        state.IsPlaying.Should().BeFalse();
        state.Capabilities.Should().Be(PlaybackCapabilities.None);
    }

    [Fact]
    public async Task Submit_WithStaleSequence_IsAcceptedButNotApplied()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(5)).Should().Be(LocalPublishResult.Accepted);
        await WaitForUpdatesAsync(1);

        _ingest.Submit(CONNECTION_A, _session, Update(5)).Should().Be(LocalPublishResult.Accepted);
        _ingest.Submit(CONNECTION_A, _session, Update(4)).Should().Be(LocalPublishResult.Accepted);
        await Task.Delay(ABSENCE_WAIT);
        _applier.Updates.Should().HaveCount(1);

        _ingest.Submit(CONNECTION_A, _session, Update(6) with { PositionMs = 120_000 })
            .Should().Be(LocalPublishResult.Accepted);
        IReadOnlyList<PlaybackUpdate> updates = await WaitForUpdatesAsync(2);
        updates[1].State.Progress.Should().Be(TimeSpan.FromMilliseconds(120_000));
    }

    [Fact]
    public async Task Submit_SequenceRestartsOnANewConnection()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(9)).Should().Be(LocalPublishResult.Accepted);
        await WaitForUpdatesAsync(1);
        Disconnect(CONNECTION_A);

        _ingest.Submit(CONNECTION_B, _session, Update(1) with { PositionMs = 7_000 })
            .Should().Be(LocalPublishResult.Accepted);

        IReadOnlyList<PlaybackUpdate> updates = await WaitForUpdatesAsync(2);
        updates[1].State.Progress.Should().Be(TimeSpan.FromMilliseconds(7_000));
    }

    [Fact]
    public void Submit_OverTheTokenBucket_IsRateLimitedUntilItRefills()
    {
        long sequence = 0;
        for (int i = 0; i < LocalPlaybackIngest.TOKEN_BUCKET_BURST; i++)
        {
            _ingest.Submit(CONNECTION_A, _session, Update(++sequence)).Should().Be(LocalPublishResult.Accepted);
        }

        _ingest.Submit(CONNECTION_A, _session, Update(++sequence)).Should().Be(LocalPublishResult.RateLimited);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        _ingest.Submit(CONNECTION_A, _session, Update(++sequence)).Should().Be(LocalPublishResult.Accepted);
        _ingest.Submit(CONNECTION_A, _session, Update(++sequence)).Should().Be(LocalPublishResult.RateLimited);

        _time.Advance(TimeSpan.FromMinutes(1));
        for (int i = 0; i < LocalPlaybackIngest.TOKEN_BUCKET_BURST; i++)
        {
            _ingest.Submit(CONNECTION_A, _session, Update(++sequence)).Should().Be(LocalPublishResult.Accepted);
        }

        _ingest.Submit(CONNECTION_A, _session, Update(++sequence))
            .Should().Be(LocalPublishResult.RateLimited, "the bucket holds at most a burst");
    }

    [Fact]
    public void Submit_TokenBucketIsPerConnection()
    {
        UserSession other = LocalSession("local-2");
        for (int i = 1; i <= LocalPlaybackIngest.TOKEN_BUCKET_BURST + 1; i++)
        {
            _ingest.Submit(CONNECTION_A, _session, Update(i));
        }

        _ingest.Submit(CONNECTION_B, other, Update(1)).Should().Be(LocalPublishResult.Accepted);
    }

    [Fact]
    public async Task Submit_FromANewerConnectionOfTheSession_TakesThePublisherClaim()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);
        await WaitForUpdatesAsync(1);

        _ingest.Submit(CONNECTION_B, _session, Update(1) with { PositionMs = 7_000 })
            .Should().Be(LocalPublishResult.Accepted);
        (await WaitForUpdatesAsync(2))[1].State.Progress.Should().Be(TimeSpan.FromMilliseconds(7_000));

        _ingest.Submit(CONNECTION_A, _session, Update(2))
            .Should().Be(LocalPublishResult.NotPublisher, "the connection whose claim was taken must not take it back");
        _ingest.Submit(CONNECTION_B, _session, Update(2)).Should().Be(LocalPublishResult.Accepted);
    }

    [Fact]
    public async Task Submit_AfterAReconnect_IsNotFrozenWhenTheHalfOpenOldConnectionTimesOut()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        await WaitForUpdatesAsync(1);

        _ingest.Submit(CONNECTION_B, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);
        await WaitForUpdatesAsync(2);
        _time.Advance(TimeSpan.FromSeconds(30));
        Disconnect(CONNECTION_A);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS * 2));

        await Task.Delay(ABSENCE_WAIT);
        _applier.OfflineSessions.Should().BeEmpty("the live publisher still holds the claim");
    }

    [Fact]
    public void Submit_FromADisplacedConnection_TakesTheClaimBackOnceItIsFree()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        _ingest.Submit(CONNECTION_B, _session, Update(1));
        _ingest.Submit(CONNECTION_A, _session, Update(2)).Should().Be(LocalPublishResult.NotPublisher);

        Disconnect(CONNECTION_B);

        _ingest.Submit(CONNECTION_A, _session, Update(3)).Should().Be(LocalPublishResult.Accepted);
        _ingest.Submit(CONNECTION_C, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);
        _ingest.Submit(CONNECTION_A, _session, Update(4)).Should().Be(LocalPublishResult.NotPublisher);
    }

    [Fact]
    public async Task Submit_FromAConnectionAlreadyClosed_IsRefusedAndHoldsNoClaim()
    {
        Disconnect(CONNECTION_A);

        _ingest.Submit(CONNECTION_A, _session, Update(1)).Should().Be(LocalPublishResult.NotPublisher);

        _ingest.Submit(CONNECTION_B, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);
        await WaitForUpdatesAsync(1);
        Disconnect(CONNECTION_B);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS));
        await WaitUntilAsync(() => _applier.OfflineSessions.Count > 0);
        _applier.Updates.Should().HaveCount(1, "the closed connection's publish was never applied");
    }

    [Fact]
    public void Submit_PublisherClaimIsPerSession()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);

        _ingest.Submit(CONNECTION_B, LocalSession("local-2"), Update(1)).Should().Be(LocalPublishResult.Accepted);
    }

    [Theory]
    [InlineData(-15_000, true)]
    [InlineData(-15_001, false)]
    [InlineData(0, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public async Task Submit_KeepsTheClientTimestampOnlyWithinTheTolerance(long offsetFromNowMs, bool kept)
    {
        DateTimeOffset now = _time.GetUtcNow();
        long sampledMs = now.ToUnixTimeMilliseconds() + offsetFromNowMs;

        _ingest.Submit(CONNECTION_A, _session, Update(1) with { PositionAtServerMs = sampledMs });

        PlaybackState state = (await WaitForUpdatesAsync(1))[0].State;
        state.TimestampUtc.Should().Be(kept ? DateTimeOffset.FromUnixTimeMilliseconds(sampledMs) : now);
    }

    [Fact]
    public async Task Submit_WithoutAClientTimestamp_UsesTheReceiptTime()
    {
        _time.Advance(TimeSpan.FromSeconds(3));

        _ingest.Submit(CONNECTION_A, _session, Update(1) with { PositionAtServerMs = null });

        (await WaitForUpdatesAsync(1))[0].State.TimestampUtc.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task Submit_WhileAnApplyIsRunning_KeepsOnlyTheNewestUpdate()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _applier.OnApply = async update =>
        {
            if (update.State.Progress == TimeSpan.FromMilliseconds(1_000))
            {
                firstStarted.TrySetResult();
                await release.Task;
            }
        };

        _ingest.Submit(CONNECTION_A, _session, Update(1) with { PositionMs = 1_000 });
        await firstStarted.Task.WaitAsync(WAIT_TIMEOUT);
        _ingest.Submit(CONNECTION_A, _session, Update(2) with { PositionMs = 2_000 });
        _ingest.Submit(CONNECTION_A, _session, Update(3) with { PositionMs = 3_000 });
        _ingest.Submit(CONNECTION_A, _session, Update(4) with { PositionMs = 4_000 });
        release.TrySetResult();

        IReadOnlyList<PlaybackUpdate> updates = await WaitForUpdatesAsync(2);
        await Task.Delay(ABSENCE_WAIT);
        _applier.Updates.Select(u => u.State.Progress.TotalMilliseconds).Should().Equal(1_000, 4_000);
        updates.Should().HaveCount(2);
    }

    [Fact]
    public async Task GracePeriodEndingWhileAnApplyIsRunning_StillAppliesThePublishersLastUpdateFirst()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _applier.OnApply = async update =>
        {
            if (update.State.Progress == TimeSpan.FromMilliseconds(1_000))
            {
                firstStarted.TrySetResult();
                await release.Task;
            }
        };

        _ingest.Submit(CONNECTION_A, _session, Update(1) with { PositionMs = 1_000 });
        await firstStarted.Task.WaitAsync(WAIT_TIMEOUT);
        _ingest.Submit(CONNECTION_A, _session, TrackUpdate(2, "Next track") with { PositionMs = 2_000 });
        Disconnect(CONNECTION_A);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS));
        await Task.Delay(ABSENCE_WAIT);
        release.TrySetResult();

        await WaitUntilAsync(() => _applier.OfflineSessions.Count > 0);
        _applier.Updates.Select(u => u.State.CurrentTrack!.Title).Should().Equal("Glimpse of Us", "Next track");
        _applier.Events.Should().Equal("apply", "apply", "offline");
    }

    [Fact]
    public async Task Submit_OverTheLyricBudget_AppliesTrackChangesWithoutLyricsUntilTheWindowPasses()
    {
        long sequence = 0;
        for (int i = 0; i < LocalPlaybackIngest.LYRIC_FETCHES_PER_MINUTE; i++)
        {
            await SubmitAndWaitAsync(TrackUpdate(++sequence, $"Song {i}"));
            await SubmitAndWaitAsync(TrackUpdate(++sequence, $"Song {i}") with { PositionMs = 5_000 });
        }

        _applier.Updates.Should().OnlyContain(u => u.AllowLyricsFetch);

        PlaybackUpdate overBudget = await SubmitAndWaitAsync(TrackUpdate(++sequence, "One too many"));
        overBudget.AllowLyricsFetch.Should().BeFalse();

        _time.Advance(TimeSpan.FromMinutes(1));
        PlaybackUpdate afterWindow = await SubmitAndWaitAsync(TrackUpdate(++sequence, "Next minute"));
        afterWindow.AllowLyricsFetch.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseConnection_OfThePublisher_MarksItOfflineAfterTheGracePeriod()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        await WaitForUpdatesAsync(1);

        Disconnect(CONNECTION_A);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS - 1));
        await Task.Delay(ABSENCE_WAIT);
        _applier.OfflineSessions.Should().BeEmpty();

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => _applier.OfflineSessions.Count > 0);
        _applier.OfflineSessions.Should().Equal(_session.Id);
    }

    [Fact]
    public async Task ReleaseConnection_ThenANewPublisherWithinGrace_DoesNotMarkItOffline()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        await WaitForUpdatesAsync(1);

        Disconnect(CONNECTION_A);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS / 2));
        _ingest.Submit(CONNECTION_B, _session, Update(1)).Should().Be(LocalPublishResult.Accepted);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS * 2));

        await WaitForUpdatesAsync(2);
        await Task.Delay(ABSENCE_WAIT);
        _applier.OfflineSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseConnection_OfANonPublisher_DoesNotStartTheGracePeriod()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        _ingest.Submit(CONNECTION_B, _session, Update(1));
        _ingest.Submit(CONNECTION_A, _session, Update(2)).Should().Be(LocalPublishResult.NotPublisher);
        await WaitForUpdatesAsync(1);

        Disconnect(CONNECTION_A);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS * 2));

        await Task.Delay(ABSENCE_WAIT);
        _applier.OfflineSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Forget_CancelsTheGracePeriod()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        await WaitForUpdatesAsync(1);
        Disconnect(CONNECTION_A);

        _ingest.Forget(_session.Id);
        _time.Advance(TimeSpan.FromSeconds(GRACE_SECONDS * 2));

        await Task.Delay(ABSENCE_WAIT);
        _applier.OfflineSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_ForAForgottenSession_IsRefusedSoALatePublishCannotRecreateIt()
    {
        _ingest.Submit(CONNECTION_A, _session, Update(1));
        await WaitForUpdatesAsync(1);

        _ingest.Forget(_session.Id);

        _ingest.Submit(CONNECTION_A, _session, Update(2)).Should().Be(LocalPublishResult.NotLocalSession);
        _ingest.Submit(CONNECTION_B, _session, Update(1)).Should().Be(LocalPublishResult.NotLocalSession);
        await Task.Delay(ABSENCE_WAIT);
        _applier.Updates.Should().HaveCount(1);
    }

    [Fact]
    public void Forget_RefusesOnlyTheForgottenSession()
    {
        _ingest.Forget(_session.Id);

        _ingest.Submit(CONNECTION_B, LocalSession("local-2"), Update(1)).Should().Be(LocalPublishResult.Accepted);
    }

    [Fact]
    public async Task Apply_TouchesTheSessionAtMostOncePerHour()
    {
        await SubmitAndWaitAsync(Update(1));
        await SubmitAndWaitAsync(Update(2) with { PositionMs = 1_000 });
        _time.Advance(TimeSpan.FromMinutes(61));
        await SubmitAndWaitAsync(Update(3) with { PositionMs = 2_000 });

        _localSessions.Verify(s => s.TouchAsync(_session.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>Mirrors the hub: the registry drops the connection before the ingest releases it.</summary>
    private void Disconnect(string connectionId)
    {
        _registry.UnregisterConnection(connectionId);
        _ingest.ReleaseConnection(connectionId);
    }

    private async Task<PlaybackUpdate> SubmitAndWaitAsync(LocalPlaybackUpdateDto update)
    {
        int expected = _applier.Updates.Count + 1;
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _ingest.Submit(CONNECTION_A, _session, update).Should().Be(LocalPublishResult.Accepted);
        IReadOnlyList<PlaybackUpdate> updates = await WaitForUpdatesAsync(expected);
        return updates[^1];
    }

    private async Task<IReadOnlyList<PlaybackUpdate>> WaitForUpdatesAsync(int count)
    {
        await WaitUntilAsync(() => _applier.Updates.Count >= count);
        return _applier.Updates.ToList();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(WAIT_TIMEOUT);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static UserSession LocalSession(string id) =>
        new() { Id = id, Kind = SessionKind.Local, DisplayName = "Desktop " + id };

    private static LocalPlaybackUpdateDto TrackUpdate(long sequence, string title) =>
        Update(sequence) with { Track = Update(sequence).Track! with { Title = title } };

    private static LocalPlaybackUpdateDto Update(long sequence)
    {
        return new LocalPlaybackUpdateDto
        {
            Sequence = sequence,
            Player = new LocalPlayerDto { Id = "spotify", Name = "Spotify" },
            DeviceName = "Spotify",
            Track = new LocalTrackDto
            {
                Title = "Glimpse of Us",
                Artist = "Joji",
                Album = "SMITHEREENS",
                DurationMs = DURATION_MS,
                AlbumArtUrl = "https://i.scdn.co/image/abc",
                SpotifyTrackId = "2mlNgAeIBnL78ZriXgrRHz"
            },
            IsPlaying = true,
            PositionMs = 98_774,
            Rate = 1.0,
            Capabilities = new PlaybackCapabilitiesDto
            {
                CanPlay = true,
                CanPause = true,
                CanGoNext = true,
                CanGoPrevious = false,
                CanSeek = true
            }
        };
    }

    private sealed class RecordingApplier : IPlaybackUpdateApplier
    {
        private readonly ConcurrentQueue<PlaybackUpdate> _updates = new();
        private readonly ConcurrentQueue<string> _offline = new();
        private readonly ConcurrentQueue<string> _events = new();

        public Func<PlaybackUpdate, Task>? OnApply { get; set; }

        public IReadOnlyCollection<PlaybackUpdate> Updates => _updates;

        public IReadOnlyCollection<string> OfflineSessions => _offline;

        public IReadOnlyCollection<string> Events => _events;

        public async Task ApplyAsync(
            PlaybackUpdate update,
            ILyricsProvider lyricsProvider,
            ILyricsCacheRepository lyricsCache,
            CancellationToken cancellationToken = default)
        {
            if (OnApply is not null)
            {
                await OnApply(update);
            }

            _updates.Enqueue(update);
            _events.Enqueue("apply");
        }

        public Task MarkPublisherOfflineAsync(
            string sessionId,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            _offline.Enqueue(sessionId);
            _events.Enqueue("offline");
            return Task.CompletedTask;
        }

        public void Forget(string sessionId)
        {
        }
    }
}
