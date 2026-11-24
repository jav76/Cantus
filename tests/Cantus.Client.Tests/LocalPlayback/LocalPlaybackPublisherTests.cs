using System;
using System.Linq;
using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Cantus.Client.Tests.LocalPlayback.LocalPlayers;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class LocalPlaybackPublisherTests : IDisposable
{
    private const string SPOTIFY = "org.mpris.MediaPlayer2.spotify";
    private const string BRAVE = "org.mpris.MediaPlayer2.brave.instance2";
    private const long CLOCK_OFFSET_MS = 250;

    private static readonly DateTimeOffset START = new(2026, 11, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(START);
    private readonly FakeLocalPlayerSource _source = new();
    private readonly FakeLocalPlaybackSink _sink = new() { ServerClockOffsetMs = CLOCK_OFFSET_MS };
    private readonly InMemorySettingsStore _settings = new();
    private readonly LocalPlaybackPublisher _publisher;
    private TimeSpan _startedAt = TimeSpan.MinValue;

    public LocalPlaybackPublisherTests()
    {
        _publisher = new LocalPlaybackPublisher(
            _source,
            _sink,
            new PlayerSelectionPolicy(),
            new LocalPlayerPreferences(_settings),
            _clock);
    }

    private TimeSpan Now => MonotonicClock.Now(_clock);

    public void Dispose() => _publisher.Dispose();

    [Fact]
    public void Start_PublishesAFullSnapshotOfTheFollowedPlayer()
    {
        // Arrange
        LocalTrack track = new(
            "Glimpse of Us",
            "Joji",
            "SMITHEREENS",
            TimeSpan.FromMilliseconds(233000),
            "https://i.scdn.co/image/abc",
            "2mlNgAeIBnL78ZriXgrRHz");
        _source.SetPlayers(Spotify(track, position: TimeSpan.FromMilliseconds(98000), capturedAt: Now - TimeSpan.FromMilliseconds(774)));

        // Act
        _publisher.Start();

        // Assert
        LocalPlaybackUpdatePayload payload = _sink.Published.Should().ContainSingle().Subject;
        payload.Sequence.Should().Be(1);
        payload.Player.Id.Should().Be("spotify");
        payload.Player.Name.Should().Be("Spotify");
        payload.DeviceName.Should().Be("Spotify");
        payload.Track!.Title.Should().Be("Glimpse of Us");
        payload.Track.Artist.Should().Be("Joji");
        payload.Track.Album.Should().Be("SMITHEREENS");
        payload.Track.DurationMs.Should().Be(233000);
        payload.Track.AlbumArtUrl.Should().Be("https://i.scdn.co/image/abc");
        payload.Track.SpotifyTrackId.Should().Be("2mlNgAeIBnL78ZriXgrRHz");
        payload.IsPlaying.Should().BeTrue();
        payload.PositionMs.Should().Be(98774);
        payload.Rate.Should().Be(1.0);
        payload.PositionAtServerMs.Should().Be(START.ToUnixTimeMilliseconds() + CLOCK_OFFSET_MS);
        payload.Capabilities.CanPlay.Should().BeTrue();
        payload.Capabilities.CanSeek.Should().BeTrue();
        _publisher.FollowedPlayer!.InstanceId.Should().Be(SPOTIFY);
    }

    [Fact]
    public void BeforeTheFirstClockSample_SendsNoServerTimestamp()
    {
        // Arrange
        _sink.ServerClockOffsetMs = null;
        _source.SetPlayers(Spotify(Song()));

        // Act
        _publisher.Start();

        // Assert
        _sink.Published.Should().ContainSingle().Which.PositionAtServerMs.Should().BeNull();
    }

    [Fact]
    public void WithNoPlayer_PublishesNoTrack()
    {
        // Act
        _publisher.Start();

        // Assert
        LocalPlaybackUpdatePayload payload = _sink.Published.Should().ContainSingle().Subject;
        payload.Track.Should().BeNull();
        payload.Player.Id.Should().Be(LocalPlaybackPublisher.NO_PLAYER_ID);
        payload.IsPlaying.Should().BeFalse();
        payload.PositionMs.Should().Be(0);
    }

    [Fact]
    public void ANewConnection_RestartsTheSequenceWithAFullSnapshot()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _sink.Published.Should().HaveCount(2);
        _sink.Published[1].Sequence.Should().Be(2);

        // Act
        _sink.ConnectionId = "connection-2";
        _sink.RaiseConnected();

        // Assert
        _sink.Published.Should().HaveCount(3);
        LocalPlaybackUpdatePayload snapshot = _sink.Published[2];
        snapshot.Sequence.Should().Be(1);
        snapshot.Track!.Title.Should().Be("Ballin");
        snapshot.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void APublishOnANewConnectionBeforeConnectedIsRaised_StillRestartsTheSequence()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _sink.IsConnected = false;
        _source.SetPlayers(Spotify(Song("Next", "Artist"), LocalPlayerStatus.Paused));

        // Act: the hub reports Connected on the new connection before the client raises Connected.
        _sink.ConnectionId = "connection-2";
        _sink.IsConnected = true;
        _clock.Advance(LocalPlaybackPublisher.TICK_INTERVAL);
        _sink.RaiseConnected();

        // Assert
        _sink.Published.Select(p => p.Sequence).Should().Equal(1, 2, 1, 2);
        _sink.Published[2].Track!.Title.Should().Be("Next");
    }

    [Fact]
    public void Connected_OnTheSameConnection_SendsASnapshotWithTheNextSequence()
    {
        // Arrange: the first publish can go out before Connected is raised for the first connection.
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _sink.RaiseConnected();

        // Assert
        _sink.Published.Select(p => p.Sequence).Should().Equal(1, 2);
    }

    [Fact]
    public async Task AResultFromAnEarlierConnection_IsIgnored()
    {
        // Arrange
        TaskCompletionSource<int> inFlight = new();
        _sink.Pending = inFlight;
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act: the connection changes while the first publish is in flight, and that publish fails.
        _sink.ConnectionId = "connection-2";
        _sink.RaiseConnected();
        inFlight.SetResult((int)LocalPublishResult.RateLimited);

        // Assert: the snapshot goes out on the new connection without waiting out a backoff.
        await WaitUntilAsync(() => _sink.Published.Count >= 2);
        _sink.Published.Should().HaveCount(2);
        _sink.Published[1].Sequence.Should().Be(1);
    }

    [Fact]
    public void WithoutAConnection_WaitsForOneAndThenPublishes()
    {
        // Arrange
        _sink.IsConnected = false;
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();
        _clock.Advance(TimeSpan.FromSeconds(3));
        _sink.Published.Should().BeEmpty();

        // Act
        _sink.IsConnected = true;
        _sink.RaiseConnected();

        // Assert
        _sink.Published.Should().ContainSingle().Which.Sequence.Should().Be(1);
    }

    [Fact]
    public void ASteadyPlayer_IsNotRepublished()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act: the source re-reads the position every 5 s and finds it where it projected.
        for (int i = 0; i < 4; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            _source.SetPlayers(Spotify(Song(), position: Now - _startedAt, capturedAt: Now));
        }

        // Assert
        _sink.Published.Should().ContainSingle();
    }

    [Theory]
    [InlineData(300, 1)]
    [InlineData(-390, 1)]
    [InlineData(450, 2)]
    [InlineData(-1200, 2)]
    public void APositionReread_RepublishesOnlyPastTheDriftThreshold(int driftMs, int expectedPublishes)
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();
        _clock.Advance(TimeSpan.FromSeconds(5));

        // Act
        _source.SetPlayers(Spotify(
            Song(),
            position: Now - _startedAt + TimeSpan.FromMilliseconds(driftMs),
            capturedAt: Now));

        // Assert
        _sink.Published.Should().HaveCount(expectedPublishes);
    }

    [Fact]
    public void StatusTrackSeekAndCapabilityChanges_EachPublish()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _source.SetPlayers(Spotify(Song("Next", "Artist"), LocalPlayerStatus.Paused));
        _source.SetPlayers(Spotify(Song("Next", "Artist"), LocalPlayerStatus.Paused, seekGeneration: 1));
        _source.SetPlayers(Spotify(
            Song("Next", "Artist"),
            LocalPlayerStatus.Paused,
            seekGeneration: 1,
            capabilities: LocalPlayerCapabilities.None));

        // Assert
        _sink.Published.Should().HaveCount(5);
        _sink.Published[1].IsPlaying.Should().BeFalse();
        _sink.Published[2].Track!.Title.Should().Be("Next");
        _sink.Published[4].Capabilities.CanPlay.Should().BeFalse();
        _sink.Published.Should().BeInAscendingOrder(p => p.Sequence);
    }

    [Fact]
    public async Task ChangesDuringAPublish_CollapseIntoOneUpdateWithTheLatestState()
    {
        // Arrange
        TaskCompletionSource<int> inFlight = new();
        _sink.Pending = inFlight;
        _source.SetPlayers(Spotify(Song("First", "Artist")));
        _publisher.Start();

        // Act
        _source.SetPlayers(Spotify(Song("Second", "Artist")));
        _source.SetPlayers(Spotify(Song("Third", "Artist")));
        _clock.Advance(TimeSpan.FromSeconds(1));
        _source.SetPlayers(Spotify(Song("Fourth", "Artist")));
        _sink.Published.Should().ContainSingle("nothing more is sent while a publish is in flight");
        inFlight.SetResult((int)LocalPublishResult.Accepted);

        // Assert: the publisher resumes on the thread pool, because the test thread has a context.
        await WaitUntilAsync(() => _sink.Published.Count >= 2);
        _sink.Published.Should().HaveCount(2);
        _sink.Published[1].Track!.Title.Should().Be("Fourth");
        _sink.Published[1].Sequence.Should().Be(2);
    }

    [Fact]
    public void RateLimited_BacksOffFromOneSecondDoublingToThirtySeconds()
    {
        // Arrange
        _sink.EnqueueResults(
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited);
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act & Assert
        int[] expectedDelaysSeconds = [1, 2, 4, 8, 16, 30, 30];
        foreach (int delaySeconds in expectedDelaysSeconds)
        {
            int before = _sink.Published.Count;
            _clock.Advance(TimeSpan.FromSeconds(delaySeconds) - LocalPlaybackPublisher.TICK_INTERVAL);
            _sink.Published.Should().HaveCount(before, $"the {delaySeconds} s backoff has not elapsed");
            _clock.Advance(LocalPlaybackPublisher.TICK_INTERVAL);
            _sink.Published.Should().HaveCount(before + 1, $"the {delaySeconds} s backoff has elapsed");
        }
    }

    [Fact]
    public void RateLimited_ThenAccepted_ResetsTheBackoff()
    {
        // Arrange
        _sink.EnqueueResults(
            LocalPublishResult.RateLimited,
            LocalPublishResult.RateLimited,
            LocalPublishResult.Accepted,
            LocalPublishResult.RateLimited);
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();
        _clock.Advance(TimeSpan.FromSeconds(1));
        _clock.Advance(TimeSpan.FromSeconds(2));
        _sink.Published.Should().HaveCount(3);

        // Act
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _sink.Published.Should().HaveCount(4);
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Assert
        _sink.Published.Should().HaveCount(5, "the backoff starts again at one second");
    }

    [Fact]
    public void NotPublisher_RetriesEveryTenSeconds()
    {
        // Arrange
        _sink.EnqueueResults(LocalPublishResult.NotPublisher, LocalPublishResult.NotPublisher);
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act & Assert
        _clock.Advance(LocalPlaybackPublisher.NOT_PUBLISHER_RETRY - LocalPlaybackPublisher.TICK_INTERVAL);
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _sink.Published.Should().ContainSingle("changes wait for the retry too");
        _clock.Advance(LocalPlaybackPublisher.TICK_INTERVAL);
        _sink.Published.Should().HaveCount(2);
        _sink.Published[1].IsPlaying.Should().BeFalse("the retry sends the latest state");
        _clock.Advance(LocalPlaybackPublisher.NOT_PUBLISHER_RETRY);
        _sink.Published.Should().HaveCount(3);
    }

    [Fact]
    public void NotLocalSession_StopsUntilRestarted()
    {
        // Arrange
        _sink.EnqueueResults(LocalPublishResult.NotLocalSession);
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _clock.Advance(TimeSpan.FromMinutes(1));
        _sink.RaiseConnected();

        // Assert
        _sink.Published.Should().ContainSingle();
        _publisher.IsStoppedByServer.Should().BeTrue();

        _publisher.Start();
        _sink.Published.Should().HaveCount(2);
        _publisher.IsStoppedByServer.Should().BeFalse();
    }

    [Fact]
    public void Invalid_IsNotRetriedButTheNextChangeIsSent()
    {
        // Arrange
        _sink.EnqueueResults(LocalPublishResult.Invalid);
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _clock.Advance(TimeSpan.FromSeconds(30));
        int afterWaiting = _sink.Published.Count;
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));

        // Assert
        afterWaiting.Should().Be(1);
        _sink.Published.Should().HaveCount(2);
    }

    [Fact]
    public void NotConnected_RetriesAfterASecond()
    {
        // Arrange
        _sink.EnqueueResults(LocalPublishResult.NotConnected);
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _clock.Advance(LocalPlaybackPublisher.SEND_FAILURE_RETRY);

        // Assert
        _sink.Published.Should().HaveCount(2);
    }

    [Fact]
    public void AnUnworthyTrack_IsPublishedAsNoTrack()
    {
        // Arrange: Spotify playing an ad-length item.
        _source.SetPlayers(Spotify(Song("Advertisement", "Spotify", seconds: 15)));

        // Act
        _publisher.Start();

        // Assert
        _sink.Published.Should().ContainSingle().Which.Track.Should().BeNull();
    }

    [Fact]
    public void WhenTheFollowedPlayerLeaves_PublishesNoTrackUnderItsName()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _source.SetPlayers();

        // Assert
        _sink.Published.Should().HaveCount(2);
        _sink.Published[1].Track.Should().BeNull();
        _sink.Published[1].Player.Id.Should().Be("spotify");
    }

    [Fact]
    public void ADisabledPlayer_IsNotPublished()
    {
        // Arrange
        new LocalPlayerPreferences(_settings).SetEnabled("brave", false);
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Playing, Song(), startedAt: Now, appId: "brave");
        _source.SetPlayers(brave);

        // Act
        _publisher.Start();

        // Assert
        _sink.Published.Should().ContainSingle().Which.Track.Should().BeNull();
        _publisher.FollowedPlayer.Should().BeNull();
    }

    [Fact]
    public void ASwitchToAnotherPlayer_IsPublishedAfterTheDebounce()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();
        _clock.Advance(TimeSpan.FromSeconds(10));
        LocalPlayerSnapshot brave = Player(BRAVE, LocalPlayerStatus.Playing, Song("Video Song", "Band"), startedAt: Now, appId: "brave", capturedAt: Now);

        // Act
        _source.SetPlayers(Spotify(Song()), brave);
        int beforeDebounce = _sink.Published.Count;
        _clock.Advance(PlayerSelectionPolicy.SWITCH_DEBOUNCE);

        // Assert
        beforeDebounce.Should().Be(1);
        _sink.Published.Should().HaveCount(2);
        _sink.Published[1].Player.Id.Should().Be("brave");
        _sink.Published[1].Track!.Title.Should().Be("Video Song");
    }

    [Fact]
    public void LongText_IsTruncatedToTheContractLimits()
    {
        // Arrange
        string longText = new('a', 400);
        LocalTrack track = new(longText, longText, longText, TimeSpan.FromMinutes(3), "https://x/" + new string('b', 2100), null);
        _source.SetPlayers(Spotify(track) with { AppName = new string('n', 200) });

        // Act
        _publisher.Start();

        // Assert
        LocalPlaybackUpdatePayload payload = _sink.Published.Should().ContainSingle().Subject;
        payload.Track!.Title.Should().HaveLength(LocalPlaybackPublisher.MAX_TRACK_TEXT_LENGTH);
        payload.Track.Artist.Should().HaveLength(LocalPlaybackPublisher.MAX_TRACK_TEXT_LENGTH);
        payload.Track.Album.Should().HaveLength(LocalPlaybackPublisher.MAX_TRACK_TEXT_LENGTH);
        payload.Track.AlbumArtUrl.Should().BeNull();
        payload.Player.Name.Should().HaveLength(LocalPlaybackPublisher.MAX_PLAYER_TEXT_LENGTH);
    }

    [Fact]
    public void Stop_StopsPublishing()
    {
        // Arrange
        _source.SetPlayers(Spotify(Song()));
        _publisher.Start();

        // Act
        _publisher.Stop();
        _source.SetPlayers(Spotify(Song(), LocalPlayerStatus.Paused));
        _clock.Advance(TimeSpan.FromSeconds(10));

        // Assert
        _sink.Published.Should().ContainSingle();
        _publisher.IsRunning.Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    // A Spotify snapshot playing since the first call, sampled now unless told otherwise.
    private LocalPlayerSnapshot Spotify(
        LocalTrack track,
        LocalPlayerStatus status = LocalPlayerStatus.Playing,
        TimeSpan? position = null,
        TimeSpan? capturedAt = null,
        int seekGeneration = 0,
        LocalPlayerCapabilities? capabilities = null)
    {
        if (_startedAt == TimeSpan.MinValue)
        {
            _startedAt = Now;
        }

        TimeSpan sampledAt = capturedAt ?? Now;
        return new LocalPlayerSnapshot(
            SPOTIFY,
            "spotify",
            "Spotify",
            status,
            track,
            position ?? (status == LocalPlayerStatus.Playing ? sampledAt - _startedAt : TimeSpan.FromSeconds(1)),
            sampledAt,
            1.0,
            capabilities ?? ALL_CAPABILITIES,
            _startedAt,
            seekGeneration);
    }
}
