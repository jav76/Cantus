using Cantus.Core.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class PlaybackSessionRegistryTests
{
    private const int BURST_SIZE = 20;

    [Fact]
    public void RegisterAndUnregister_TracksConnectedClientsCount()
    {
        PlaybackSessionRegistry registry = new();
        registry.ConnectedClientsCount.Should().Be(0);
        registry.HasConnectedClients.Should().BeFalse();

        registry.RegisterConnection("conn-1");
        registry.ConnectedClientsCount.Should().Be(1);
        registry.HasConnectedClients.Should().BeTrue();

        registry.RegisterConnection("conn-2");
        registry.ConnectedClientsCount.Should().Be(2);

        registry.UnregisterConnection("conn-1");
        registry.ConnectedClientsCount.Should().Be(1);
        registry.HasConnectedClients.Should().BeTrue();

        registry.UnregisterConnection("conn-2");
        registry.ConnectedClientsCount.Should().Be(0);
        registry.HasConnectedClients.Should().BeFalse();
    }

    [Fact]
    public void ConnectionTransitions_TriggerEvents()
    {
        PlaybackSessionRegistry registry = new();
        bool connectedFired = false;
        bool emptyFired = false;

        registry.OnClientsConnected += (_, _) => connectedFired = true;
        registry.OnClientsEmpty += (_, _) => emptyFired = true;

        registry.RegisterConnection("conn-1");
        connectedFired.Should().BeTrue();

        registry.RegisterConnection("conn-2"); // Second connection should not trigger OnClientsConnected again

        registry.UnregisterConnection("conn-1");
        emptyFired.Should().BeFalse();

        registry.UnregisterConnection("conn-2");
        emptyFired.Should().BeTrue();
    }

    [Fact]
    public void Subscriptions_CanBeSetAndRetrieved()
    {
        PlaybackSessionRegistry registry = new();
        registry.RegisterConnection("conn-1");

        registry.GetConnectionSubscription("conn-1").Should().BeNull();

        registry.SetConnectionSubscription("conn-1", "user-123");
        registry.GetConnectionSubscription("conn-1").Should().Be("user-123");

        registry.SetConnectionSubscription("conn-1", null);
        registry.GetConnectionSubscription("conn-1").Should().BeNull();
    }

    [Fact]
    public void UpdateUserState_StoresAndRetrievesSnapshot()
    {
        PlaybackSessionRegistry registry = new();
        PlaybackState playback = new()
        {
            CurrentTrack = new TrackInfo
            {
                Id = "track-1",
                Title = "Song A",
                Artist = "Artist A",
                Duration = TimeSpan.FromMinutes(3)
            },
            IsPlaying = true,
            Progress = TimeSpan.FromSeconds(30),
            TimestampUtc = DateTimeOffset.UtcNow
        };

        SyncedLyrics lyrics = new()
        {
            TrackId = "track-1",
            Title = "Song A",
            Artist = "Artist A",
            Lines = [new LyricLine(TimeSpan.FromSeconds(5), "Hello")]
        };

        registry.UpdateUserState("user-1", "Alice", playback, lyrics, 250);

        UserPlaybackSnapshot? snapshot = registry.GetUserState("user-1");
        snapshot.Should().NotBeNull();
        snapshot!.UserId.Should().Be("user-1");
        snapshot.DisplayName.Should().Be("Alice");
        snapshot.PlaybackState!.CurrentTrack!.Title.Should().Be("Song A");
        snapshot.Lyrics!.Lines.Should().HaveCount(1);
        snapshot.TrackOffsetMs.Should().Be(250);
    }

    [Fact]
    public void GetActivePlaybackSnapshot_PrioritizesPlayingUser()
    {
        PlaybackSessionRegistry registry = new();

        PlaybackState pausedPlayback = new()
        {
            CurrentTrack = new TrackInfo { Id = "track-1", Title = "Paused Song", Artist = "Artist 1" },
            IsPlaying = false
        };

        PlaybackState activePlayback = new()
        {
            CurrentTrack = new TrackInfo { Id = "track-2", Title = "Active Song", Artist = "Artist 2" },
            IsPlaying = true
        };

        registry.UpdateUserState("user-paused", "Bob", pausedPlayback, null, 0);
        registry.UpdateUserState("user-active", "Charlie", activePlayback, null, 0);

        UserPlaybackSnapshot? activeSnapshot = registry.GetActivePlaybackSnapshot();
        activeSnapshot.Should().NotBeNull();
        activeSnapshot!.UserId.Should().Be("user-active");
        activeSnapshot.DisplayName.Should().Be("Charlie");
    }

    [Fact]
    public void GetActiveUserIdsWithConnectedClients_ReturnsDistinctAuthenticatedUsers()
    {
        PlaybackSessionRegistry registry = new();

        registry.RegisterConnection("conn-1", "user-1");
        registry.RegisterConnection("conn-2", "user-1"); // Multiple connections for same user
        registry.RegisterConnection("conn-3", "user-2");
        registry.RegisterConnection("conn-4", null); // Unauthenticated client

        IReadOnlyCollection<string> activeUsers = registry.GetActiveUserIdsWithConnectedClients();
        activeUsers.Should().BeEquivalentTo(new[] { "user-1", "user-2" });

        registry.UnregisterConnection("conn-1");
        // user-1 still has conn-2
        registry.GetActiveUserIdsWithConnectedClients().Should().BeEquivalentTo(new[] { "user-1", "user-2" });

        registry.UnregisterConnection("conn-2");
        // user-1 has no remaining connections
        registry.GetActiveUserIdsWithConnectedClients().Should().BeEquivalentTo(new[] { "user-2" });
    }

    [Fact]
    public void VisibilityTracking_ReportsCorrectUserVisibility()
    {
        PlaybackSessionRegistry registry = new();

        registry.RegisterConnection("conn-1", "user-1");
        registry.RegisterConnection("conn-2", "user-1");

        // Initially visible
        registry.IsUserVisible("user-1").Should().BeTrue();

        // One tab hidden, other still visible -> user is visible
        registry.SetConnectionVisibility("conn-1", false);
        registry.IsUserVisible("user-1").Should().BeTrue();

        // Both tabs hidden -> user is not visible
        registry.SetConnectionVisibility("conn-2", false);
        registry.IsUserVisible("user-1").Should().BeFalse();

        // One tab becomes visible again -> user is visible
        registry.SetConnectionVisibility("conn-1", true);
        registry.IsUserVisible("user-1").Should().BeTrue();
    }

    [Fact]
    public void RequestUserActivity_RaisesOnUserActivityRequested()
    {
        PlaybackSessionRegistry registry = new();
        string? signaledUser = null;
        registry.OnUserActivityRequested += (_, uid) => signaledUser = uid;

        registry.RequestUserActivity("user-99");
        signaledUser.Should().Be("user-99");
    }

    [Fact]
    public void RequestDisplayActivity_InABurst_RaisesOncePerOwner()
    {
        FakeTimeProvider timeProvider = new();
        PlaybackSessionRegistry registry = new(timeProvider);
        List<string> signaled = new();
        registry.OnUserActivityRequested += (_, uid) => signaled.Add(uid);

        for (int i = 0; i < BURST_SIZE; i++)
        {
            registry.RequestDisplayActivity("user-1");
            registry.RequestDisplayActivity("user-2");
        }

        signaled.Should().Equal("user-1", "user-2");
    }

    [Fact]
    public void RequestDisplayActivity_AfterMinimumInterval_RaisesAgain()
    {
        FakeTimeProvider timeProvider = new();
        PlaybackSessionRegistry registry = new(timeProvider);
        int signaledCount = 0;
        registry.OnUserActivityRequested += (_, _) => signaledCount++;

        registry.RequestDisplayActivity("user-1");
        timeProvider.Advance(TimeSpan.FromMilliseconds(PlaybackSessionRegistry.DISPLAY_ACTIVITY_MIN_INTERVAL_MS));
        registry.RequestDisplayActivity("user-1");

        signaledCount.Should().Be(2);
    }

    [Fact]
    public void RequestUserActivity_IsNotThrottledByDisplayActivity()
    {
        FakeTimeProvider timeProvider = new();
        PlaybackSessionRegistry registry = new(timeProvider);
        int signaledCount = 0;
        registry.OnUserActivityRequested += (_, _) => signaledCount++;

        registry.RequestDisplayActivity("user-1");
        registry.RequestUserActivity("user-1");
        registry.RequestUserActivity("user-1");

        signaledCount.Should().Be(3);
    }

    private static PlaybackState PlayingTrack(string trackId) => new()
    {
        CurrentTrack = new TrackInfo { Id = trackId, Title = "Song " + trackId, Artist = "Artist" },
        IsPlaying = true
    };

    private static SyncedLyrics LyricsFor(string trackId, string text) => new()
    {
        TrackId = trackId,
        Title = "Song " + trackId,
        Artist = "Artist",
        Lines = [new LyricLine(TimeSpan.Zero, text)]
    };

    [Fact]
    public void UpdateUserPlayback_KeepsTheLyricsAndOffsetThatAreInTheSnapshotWhenItWrites()
    {
        PlaybackSessionRegistry registry = new();
        registry.UpdateUserState("user-1", "Alice", PlayingTrack("track-1"), LyricsFor("track-1", "Old"), 250);
        SyncedLyrics replaced = LyricsFor("track-1", "New");
        registry.ReplaceLyricsForTrack("track-1", replaced);
        PlaybackState later = PlayingTrack("track-1");

        registry.UpdateUserPlayback("user-1", "Alice", later);

        UserPlaybackSnapshot snapshot = registry.GetUserState("user-1")!;
        snapshot.PlaybackState.Should().BeSameAs(later);
        snapshot.Lyrics.Should().BeSameAs(replaced);
        snapshot.TrackOffsetMs.Should().Be(250);
    }

    [Fact]
    public void UpdateUserPlayback_ForAUserWithNoSnapshot_StoresThePlaybackWithoutLyrics()
    {
        PlaybackSessionRegistry registry = new();

        registry.UpdateUserPlayback("user-1", "Alice", PlayingTrack("track-1"));

        UserPlaybackSnapshot snapshot = registry.GetUserState("user-1")!;
        snapshot.PlaybackState!.CurrentTrack!.Id.Should().Be("track-1");
        snapshot.Lyrics.Should().BeNull();
        snapshot.TrackOffsetMs.Should().Be(0);
    }

    [Fact]
    public void ReplaceLyricsForTrack_UpdatesOnlySnapshotsOnThatTrackAndKeepsTheirPlayback()
    {
        PlaybackSessionRegistry registry = new();
        PlaybackState firstPlayback = PlayingTrack("track-1");
        SyncedLyrics otherLyrics = LyricsFor("track-2", "Other");
        registry.UpdateUserState("user-1", "Alice", firstPlayback, LyricsFor("track-1", "Old"), 100);
        registry.UpdateUserState("user-2", "Bob", PlayingTrack("track-1"), null, 0);
        registry.UpdateUserState("user-3", "Carol", PlayingTrack("track-2"), otherLyrics, 0);
        SyncedLyrics replaced = LyricsFor("track-1", "New");

        IReadOnlyList<string> updated = registry.ReplaceLyricsForTrack("track-1", replaced);

        updated.Should().BeEquivalentTo(new[] { "user-1", "user-2" });
        UserPlaybackSnapshot first = registry.GetUserState("user-1")!;
        first.Lyrics.Should().BeSameAs(replaced);
        first.PlaybackState.Should().BeSameAs(firstPlayback);
        first.TrackOffsetMs.Should().Be(100);
        registry.GetUserState("user-2")!.Lyrics.Should().BeSameAs(replaced);
        registry.GetUserState("user-3")!.Lyrics.Should().BeSameAs(otherLyrics);
    }

    [Fact]
    public void ReplaceLyricsForTrack_WithNoLyrics_ClearsTheLyricsOnThatTrack()
    {
        PlaybackSessionRegistry registry = new();
        registry.UpdateUserState("user-1", "Alice", PlayingTrack("track-1"), LyricsFor("track-1", "Old"), 0);

        registry.ReplaceLyricsForTrack("track-1", null);

        registry.GetUserState("user-1")!.Lyrics.Should().BeNull();
    }

    [Fact]
    public void ReplaceLyricsForTrack_AfterTheUserMovedToAnotherTrack_LeavesTheNewTrackAlone()
    {
        PlaybackSessionRegistry registry = new();
        registry.UpdateUserState("user-1", "Alice", PlayingTrack("track-1"), LyricsFor("track-1", "Old"), 0);
        SyncedLyrics nextLyrics = LyricsFor("track-2", "Next");
        registry.UpdateUserState("user-1", "Alice", PlayingTrack("track-2"), nextLyrics, 0);

        IReadOnlyList<string> updated = registry.ReplaceLyricsForTrack("track-1", LyricsFor("track-1", "New"));

        updated.Should().BeEmpty();
        UserPlaybackSnapshot snapshot = registry.GetUserState("user-1")!;
        snapshot.PlaybackState!.CurrentTrack!.Id.Should().Be("track-2");
        snapshot.Lyrics.Should().BeSameAs(nextLyrics);
    }
}
