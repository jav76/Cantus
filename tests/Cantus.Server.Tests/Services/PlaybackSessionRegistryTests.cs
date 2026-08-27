using Cantus.Core.Models;
using Cantus.Server.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class PlaybackSessionRegistryTests
{
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
}
