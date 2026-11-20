using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Core.Models;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelTests
{
    [Fact]
    public void FindActiveLineIndex_WithNoLines_ReturnsMinusOne()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Act
        int result = vm.FindActiveLineIndex(15000);

        // Assert
        result.Should().Be(-1);
    }

    [Fact]
    public void FindActiveLineIndex_WithMultipleLines_CorrectlyLocatesLines()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        vm.LyricLines.Add(new LyricLineViewModel { TimestampMs = 10000, Text = "Line 1" });
        vm.LyricLines.Add(new LyricLineViewModel { TimestampMs = 20000, Text = "Line 2" });
        vm.LyricLines.Add(new LyricLineViewModel { TimestampMs = 30000, Text = "Line 3" });

        // Act & Assert
        vm.FindActiveLineIndex(5000).Should().Be(-1, "Before first line");
        vm.FindActiveLineIndex(10000).Should().Be(0, "Exact match on line 1");
        vm.FindActiveLineIndex(15000).Should().Be(0, "Between line 1 and line 2");
        vm.FindActiveLineIndex(20000).Should().Be(1, "Exact match on line 2");
        vm.FindActiveLineIndex(25000).Should().Be(1, "Between line 2 and line 3");
        vm.FindActiveLineIndex(30000).Should().Be(2, "Exact match on line 3");
        vm.FindActiveLineIndex(99999).Should().Be(2, "Past the final line");
    }

    [Fact]
    public void LyricLineViewModel_VisualProperties_UpdateOnActiveAndPast()
    {
        // Arrange
        LyricLineViewModel line = new() { TimestampMs = 5000, Text = "Hello world" };

        // Act - Active
        line.IsActive = true;
        line.IsPast = false;

        // Assert
        line.FontSize.Should().Be(32.0);
        line.Opacity.Should().Be(1.0);

        // Act - Past
        line.IsActive = false;
        line.IsPast = true;

        // Assert
        line.FontSize.Should().Be(20.0);
        line.Opacity.Should().Be(0.45);

        // Act - Upcoming
        line.IsActive = false;
        line.IsPast = false;

        // Assert
        line.FontSize.Should().Be(22.0);
        line.Opacity.Should().Be(0.75);
    }

    [Fact]
    public void ToggleKioskMode_TogglesState_AndSynchronizesWithLayoutManager()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        ResponsiveLayoutManager layout = new();
        LyricsViewModel vm = new(client, ThemeManager.Instance, layout);
        vm.IsKioskMode.Should().BeFalse();
        layout.IsKioskMode.Should().BeFalse();

        // Act
        vm.ToggleKioskMode();

        // Assert
        vm.IsKioskMode.Should().BeTrue();
        layout.IsKioskMode.Should().BeTrue();
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.FullscreenTv);

        // Act
        vm.ToggleKioskMode();

        // Assert
        vm.IsKioskMode.Should().BeFalse();
        layout.IsKioskMode.Should().BeFalse();
    }

    [Fact]
    public void LayoutManagerBreakpointChange_UpdatesLyricLineFontSizes()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1440, 900); // Large
        LyricsViewModel vm = new(client, ThemeManager.Instance, layout);

        LyricLineViewModel line = new() { TimestampMs = 1000, Text = "Sing along" };
        vm.LyricLines.Add(line);

        line.IsActive = true;
        line.FontSize.Should().Be(38.0); // Large active font size

        // Act - Switch to Small
        layout.UpdateDimensions(375, 667);

        // Assert
        line.FontSize.Should().Be(24.0); // Small active font size

        // Act - Switch to FullscreenTv via the explicit kiosk toggle
        layout.IsKioskMode = true;

        // Assert
        line.FontSize.Should().Be(50.0); // TV active font size
    }

    [Fact]
    public void MobileView_Switching_UpdatesViewModelAndLayout()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        ResponsiveLayoutManager layout = new();
        LyricsViewModel vm = new(client, ThemeManager.Instance, layout);

        // Act
        vm.SetMobileView(MobileViewMode.NowPlaying);

        // Assert
        layout.MobileView.Should().Be(MobileViewMode.NowPlaying);

        // Act
        vm.CycleMobileView();

        // Assert
        layout.MobileView.Should().Be(MobileViewMode.SyncAndSettings);
    }

    [Fact]
    public void ToggleStaticLyricsMode_TogglesStateAndVisibilities()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.HasLyrics = true;

        vm.IsStaticLyricsMode.Should().BeFalse();
        vm.SyncedLyricsVisibility.Should().Be(Visibility.Visible);
        vm.StaticLyricsVisibility.Should().Be(Visibility.Collapsed);
        vm.ModeToggleText.Should().Be("Static View");

        // Act
        vm.ToggleStaticLyricsMode();

        // Assert
        vm.IsStaticLyricsMode.Should().BeTrue();
        vm.SyncedLyricsVisibility.Should().Be(Visibility.Collapsed);
        vm.StaticLyricsVisibility.Should().Be(Visibility.Visible);
        vm.ModeToggleText.Should().Be("Live Synced");
    }

    [Fact]
    public void StaticLyricsText_WhenLinesExist_GeneratesFormattedText()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.LyricLines.Add(new LyricLineViewModel { Text = "Line 1" });
        vm.LyricLines.Add(new LyricLineViewModel { Text = "Line 2" });

        // Assert
        vm.StaticLyricsText.Should().Be("Line 1\nLine 2");
    }

    [Fact]
    public async Task NudgeOffsetAsync_UpdatesOffsetDisplay()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Timing.OffsetText.Should().Be("+0.0s");

        // Act - without playing track, nudge is safe no-op
        await vm.Timing.NudgeOffsetAsync(500);

        // Assert
        vm.Timing.OffsetText.Should().Be("+0.0s");
    }

    [Fact]
    public async Task ResetOffsetAsync_ResetsOffsetDisplay()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Act
        await vm.Timing.ResetOffsetAsync();

        // Assert
        vm.Timing.OffsetText.Should().Be("+0.0s");
    }

    [Fact]
    public async Task LogoutAsync_ResetsSessionAndPlaybackState()
    {
        // Arrange
        SignalRPlaybackClient client = new("http://127.0.0.1:59999/hubs/playback");
        LyricsViewModel vm = new(client);
        vm.Session.Sessions.Add(new AuthorizedSessionPayload
        {
            Id = "user-1",
            DisplayName = "Test User"
        });
        vm.Session.AuthorizedSessionsCount = 1;
        vm.Session.ActiveUserId = "user-1";
        vm.Session.ActiveUserName = "Test User";
        vm.CurrentTitle = "Playing Song";
        vm.Transport.IsPlaying = true;

        vm.Session.CurrentUserSession.Should().NotBeNull();
        vm.Session.IsAuthorized.Should().BeTrue();

        // Act
        await vm.Session.LogoutAsync();

        // Assert
        vm.Session.Sessions.Should().BeEmpty();
        vm.Session.CurrentUserSession.Should().BeNull();
        vm.Session.AuthorizedSessionsCount.Should().Be(0);
        vm.Session.ActiveUserId.Should().BeNull();
        vm.Session.ActiveUserName.Should().Be("None");
        vm.Session.IsAuthorized.Should().BeFalse();
        vm.CurrentTitle.Should().Be("No Track Playing");
        vm.Transport.IsPlaying.Should().BeFalse();
        vm.HasLyrics.Should().BeFalse();
        vm.IsStaticLyricsMode.Should().BeFalse();
        vm.LyricLines.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyStateText_ReflectsWhetherATrackIsPlaying()
    {
        // Arrange - fresh session: nothing connected, nothing playing
        SignalRPlaybackClient client = new("http://127.0.0.1:59999/hubs/playback");
        LyricsViewModel vm = new(client);

        vm.EmptyStateTitle.Should().Be("Waiting for Lyrics...");
        vm.EmptyStateSubtitle.Should().Be("Connect Spotify and play music to see lyrics.");

        // Act - a track starts playing but no lyrics arrive for it
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Miss America", Artist = "Artist" },
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        // Assert - the placeholder must not tell an already-connected,
        // already-playing user to "connect Spotify and play music"
        vm.EmptyStateTitle.Should().Be("No Lyrics Found");
        vm.EmptyStateSubtitle.Should().Be("Lyrics for this track aren't available yet.");

        // Act - logout clears the playback state again
        await vm.Session.LogoutAsync();

        // Assert
        vm.EmptyStateTitle.Should().Be("Waiting for Lyrics...");
        vm.EmptyStateSubtitle.Should().Be("Connect Spotify and play music to see lyrics.");
    }

    [Fact]
    public void OnLyricsReceived_RaisesLyricsReloadedAfterStateReset()
    {
        // Arrange - simulate mid-song state from a previous track
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.SetUserScrollingPaused(true);

        int reloadedCount = 0;
        int activeIndexAtReload = int.MinValue;
        bool pausedAtReload = true;
        vm.LyricsReloaded += () =>
        {
            reloadedCount++;
            activeIndexAtReload = vm.ActiveLineIndex;
            pausedAtReload = vm.IsUserScrollingPaused;
        };

        // Act
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "track-next",
            Title = "Next Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "First line" }
            }
        });

        // Assert - the event fires once, after the reset, so a view scrolling
        // to the top sees a fresh collection with no active line and no pause
        reloadedCount.Should().Be(1);
        activeIndexAtReload.Should().Be(-1);
        pausedAtReload.Should().BeFalse();
    }

    [Fact]
    public void PlayPauseGlyph_TracksPlayingState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        List<string> changed = new();
        vm.Transport.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        // Assert - paused shows the play glyph
        vm.Transport.PlayPauseGlyph.Should().Be("\u25B6");

        // Act
        vm.Transport.IsPlaying = true;

        // Assert - playing shows the pause glyph, and the glyph notified
        vm.Transport.PlayPauseGlyph.Should().Be("\u23F8");
        changed.Should().Contain(nameof(PlaybackTransport.PlayPauseGlyph));
    }

    [Fact]
    public async Task TogglePlayPauseAsync_WithNoTrack_DoesNothing()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Transport.IsPlaying = true;

        // Act
        await vm.Transport.TogglePlayPauseAsync();

        // Assert - no track loaded, so no command and no state flip
        vm.Transport.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task TogglePlayPauseAsync_WhenCommandFails_DoesNotFlipPlayingStateAndShowsStatus()
    {
        // Arrange - disconnected client, so the command fails
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist" },
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        });
        vm.Transport.IsPlaying.Should().BeTrue();

        // Act
        await vm.Transport.TogglePlayPauseAsync();

        // Assert - the optimistic flip only happens on success, and the failure is surfaced
        vm.Transport.IsPlaying.Should().BeTrue();
        vm.Transport.TransportStatusText.Should().NotBeEmpty();
        vm.Transport.TransportStatusVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ReportTransportResult_MapsResultsToUserGuidance()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Assert - nothing shown by default
        vm.Transport.TransportStatusVisibility.Should().Be(Visibility.Collapsed);

        // Missing scope / non-Premium points at re-linking the account
        vm.Transport.ReportTransportResult(Cantus.Core.Models.PlayerCommandResult.MissingPermissions);
        vm.Transport.TransportStatusText.Should().Contain("reconnect");
        vm.Transport.TransportStatusVisibility.Should().Be(Visibility.Visible);

        // No active device points at starting playback
        vm.Transport.ReportTransportResult(Cantus.Core.Models.PlayerCommandResult.NoActiveDevice);
        vm.Transport.TransportStatusText.Should().Contain("device");

        // Success clears any prior message
        vm.Transport.ReportTransportResult(Cantus.Core.Models.PlayerCommandResult.Success);
        vm.Transport.TransportStatusText.Should().BeEmpty();
        vm.Transport.TransportStatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task SkipCommands_WithNoTrackOrConnection_DoNotThrow()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Act & Assert - no track: early return; with track but disconnected: graceful false
        await vm.Transport.SkipToNextAsync();
        await vm.Transport.SkipToPreviousAsync();

        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist" },
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        await vm.Transport.SkipToNextAsync();
        await vm.Transport.SkipToPreviousAsync();
    }

    [Fact]
    public void ShuffleAndRepeatState_UpdateFromPlaybackPayload()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Transport.IsShuffled.Should().BeFalse();
        vm.Transport.RepeatMode.Should().Be("off");

        // Act
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist" },
            IsPlaying = true,
            IsShuffled = true,
            RepeatMode = "context",
            TimestampUtc = DateTimeOffset.UtcNow
        });

        // Assert
        vm.Transport.IsShuffled.Should().BeTrue();
        vm.Transport.RepeatMode.Should().Be("context");
    }

    [Fact]
    public void RepeatGlyphAndButtonBrushes_TrackState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Off: repeat-all glyph, muted brushes
        vm.Transport.RepeatGlyph.Should().Be("\uE8EE");
        vm.Transport.RepeatButtonForeground.Should().BeSameAs(vm.TextMutedBrush);
        vm.Transport.ShuffleButtonForeground.Should().BeSameAs(vm.TextMutedBrush);

        // Context: still the repeat-all glyph, accent brush
        vm.Transport.RepeatMode = "context";
        vm.Transport.RepeatGlyph.Should().Be("\uE8EE");
        vm.Transport.RepeatButtonForeground.Should().BeSameAs(vm.PrimaryAccentBrush);

        // Track: repeat-one glyph
        vm.Transport.RepeatMode = "track";
        vm.Transport.RepeatGlyph.Should().Be("\uE8ED");

        vm.Transport.IsShuffled = true;
        vm.Transport.ShuffleButtonForeground.Should().BeSameAs(vm.PrimaryAccentBrush);
    }

    [Fact]
    public async Task ShuffleRepeatSeek_WhenCommandFails_DoNotChangeStateAndDoNotThrow()
    {
        // Arrange - disconnected client, so every command fails
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist", DurationMs = 200000 },
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        // Act
        await vm.Transport.ToggleShuffleAsync();
        await vm.Transport.CycleRepeatAsync();
        await vm.Transport.SeekToFractionAsync(0.5);

        // Assert - no optimistic flips without success
        vm.Transport.IsShuffled.Should().BeFalse();
        vm.Transport.RepeatMode.Should().Be("off");
        vm.Transport.TransportStatusText.Should().NotBeEmpty();
    }

    [Fact]
    public void VolumeText_MapsPercentAndUnknown()
    {
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        vm.Transport.VolumeText.Should().Be("—", "no playback state yet");
        vm.Transport.VolumePercent = 65;
        vm.Transport.VolumeText.Should().Be("65%");
        vm.Transport.VolumeSliderValue.Should().Be(65);

        // The debounce entry point clamps and never throws headless.
        vm.Transport.RequestVolumeChange(150);
    }

    [Fact]
    public void ServerBaseUrl_DerivesBaseUrlFromClient()
    {
        // Arrange
        SignalRPlaybackClient client = new("http://192.168.1.50:5000/hubs/playback");
        LyricsViewModel vm = new(client);

        // Assert
        vm.ServerBaseUrl.Should().Be("http://192.168.1.50:5000");
    }

    [Fact]
    public void OnLyricsReceived_WithPlainLyricsOnly_AutoSwitchesToStaticMode()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        LyricsPayload payload = new()
        {
            TrackId = "track-plain-1",
            Title = "Plain Song",
            Artist = "Plain Artist",
            IsSynced = false,
            Lines = new List<LyricLinePayload>(),
            PlainLyrics = "Line 1 of plain lyrics\nLine 2 of plain lyrics"
        };

        // Act
        client.RaiseLyricsReceived(payload);

        // Assert
        vm.HasLyrics.Should().BeTrue();
        vm.HasSyncedLyrics.Should().BeFalse();
        vm.HasPlainLyrics.Should().BeTrue();
        vm.IsStaticLyricsMode.Should().BeTrue();
        vm.ModeToggleVisibility.Should().Be(Visibility.Collapsed);
        vm.StaticLyricsVisibility.Should().Be(Visibility.Visible);
        vm.SyncedLyricsVisibility.Should().Be(Visibility.Collapsed);
        vm.StaticLyricsText.Should().Be("Line 1 of plain lyrics\nLine 2 of plain lyrics");
    }

    [Fact]
    public void OnLyricsReceived_WithSyncedLyrics_EnablesSyncedModeAndToggle()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        LyricsPayload payload = new()
        {
            TrackId = "track-synced-1",
            Title = "Synced Song",
            Artist = "Synced Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "First line" },
                new() { TimestampMs = 3000, Text = "Second line" }
            },
            PlainLyrics = "First line\nSecond line"
        };

        // Act
        client.RaiseLyricsReceived(payload);

        // Assert
        vm.HasLyrics.Should().BeTrue();
        vm.HasSyncedLyrics.Should().BeTrue();
        vm.HasPlainLyrics.Should().BeTrue();
        vm.IsStaticLyricsMode.Should().BeFalse();
        vm.ModeToggleVisibility.Should().Be(Visibility.Visible);
        vm.StaticLyricsVisibility.Should().Be(Visibility.Collapsed);
        vm.SyncedLyricsVisibility.Should().Be(Visibility.Visible);
        vm.LyricLines.Should().HaveCount(2);
    }

    [Fact]
    public void OnSessionsReceived_WhenEmpty_ClearsAuthorizationAndState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Session.Sessions.Add(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });
        vm.Session.AuthorizedSessionsCount = 1;
        vm.Session.ActiveUserId = "user-1";
        vm.Session.ActiveUserName = "Alice";
        vm.CurrentTitle = "Some Song";
        vm.Transport.IsPlaying = true;

        // Act
        client.RaiseSessionsReceived(new List<AuthorizedSessionPayload>());

        // Assert
        vm.Session.Sessions.Should().BeEmpty();
        vm.Session.AuthorizedSessionsCount.Should().Be(0);
        vm.Session.ActiveUserId.Should().BeNull();
        vm.Session.ActiveUserName.Should().Be("None");
        vm.Session.IsAuthorized.Should().BeFalse();
        vm.CurrentTitle.Should().Be("No Track Playing");
        vm.Transport.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void OnAuthSessionReceived_UpdatesActiveSession()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        AuthorizedSessionPayload payload = new()
        {
            Id = "user-new",
            DisplayName = "Bob",
            SpotifyUserId = "sp-bob"
        };

        // Act
        client.RaiseAuthSessionReceived(payload);

        // Assert
        vm.Session.Sessions.Should().HaveCount(1);
        vm.Session.AuthorizedSessionsCount.Should().Be(1);
        vm.Session.ActiveUserId.Should().Be("user-new");
        vm.Session.ActiveUserName.Should().Be("Bob");
        vm.Session.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void OnSessionRevoked_WhenMatchingActiveUser_ClearsSessionState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Session.Sessions.Add(new AuthorizedSessionPayload { Id = "user-revoked", DisplayName = "Charlie" });
        vm.Session.AuthorizedSessionsCount = 1;
        vm.Session.ActiveUserId = "user-revoked";
        vm.Session.ActiveUserName = "Charlie";

        // Act
        client.RaiseSessionRevoked("user-revoked");

        // Assert
        vm.Session.Sessions.Should().BeEmpty();
        vm.Session.AuthorizedSessionsCount.Should().Be(0);
        vm.Session.ActiveUserId.Should().BeNull();
        vm.Session.ActiveUserName.Should().Be("None");
        vm.Session.IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void AuthSessionReceived_NotifiesThroughTheComposedSession()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        List<string?> raised = new();
        vm.Session.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        client.RaiseAuthSessionReceived(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });

        // Assert
        vm.Session.ConnectButtonText.Should().Be("Connected: Alice");
        raised.Should().Contain(new[]
        {
            nameof(SessionViewModel.ActiveUserName),
            nameof(SessionViewModel.AuthorizedSessionsCount),
            nameof(SessionViewModel.CurrentUserSession),
            nameof(SessionViewModel.HasSessionsVisibility),
            nameof(SessionViewModel.IsAuthorized),
            nameof(SessionViewModel.ConnectButtonText),
        });
    }

    [Fact]
    public async Task SessionEnd_KeepsThePlaybackStateWhileLogoutClearsIt()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        client.RaiseAuthSessionReceived(new AuthorizedSessionPayload { Id = "user-1", DisplayName = "Alice" });
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t-1", Title = "Song", Artist = "Artist" },
            IsPlaying = true,
            ActiveUserId = "user-1",
            TimestampUtc = DateTimeOffset.UtcNow
        });

        // Act - a revoke clears the track card but not the transport's last poll
        client.RaiseSessionRevoked("user-1");

        // Assert
        vm.CurrentTitle.Should().Be("No Track Playing");
        vm.EmptyStateTitle.Should().Be("No Lyrics Found");

        // Act - logout drops the poll too
        await vm.Session.LogoutAsync();

        // Assert
        vm.EmptyStateTitle.Should().Be("Waiting for Lyrics...");
    }

    [Fact]
    public void IsAutoScrollEnabled_DefaultsToTrue_AndTogglesState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Assert default
        vm.IsAutoScrollEnabled.Should().BeTrue();
        vm.AutoScrollToggleText.Should().Be("Autoscroll");

        // Act
        vm.ToggleAutoScroll();

        // Assert toggled
        vm.IsAutoScrollEnabled.Should().BeFalse();
        vm.AutoScrollToggleText.Should().Be("Free Scroll");

        // Act again
        vm.ToggleAutoScroll();

        // Assert
        vm.IsAutoScrollEnabled.Should().BeTrue();
        vm.AutoScrollToggleText.Should().Be("Autoscroll");
    }

    [Fact]
    public void SetUserScrollingPaused_UpdatesStateAndResumeVisibility()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        LyricsPayload payload = new()
        {
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "Line 1" },
                new() { TimestampMs = 5000, Text = "Line 2" }
            }
        };
        client.RaiseLyricsReceived(payload);

        vm.IsAutoScrollEnabled = true;
        vm.IsUserScrollingPaused.Should().BeFalse();
        vm.ResumeAutoScrollVisibility.Should().Be(Visibility.Collapsed);

        // Act
        vm.SetUserScrollingPaused(true);

        // Assert
        vm.IsUserScrollingPaused.Should().BeTrue();
        vm.ResumeAutoScrollVisibility.Should().Be(Visibility.Visible);

        // Act - Resume
        vm.ResumeAutoScroll();

        // Assert
        vm.IsUserScrollingPaused.Should().BeFalse();
        vm.ResumeAutoScrollVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ResumeAutoScroll_TriggersResumedAndActiveLineEvents()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        LyricsPayload payload = new()
        {
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "Line 1" },
                new() { TimestampMs = 5000, Text = "Line 2" }
            }
        };
        client.RaiseLyricsReceived(payload);

        bool resumedFired = false;
        int activeLineReported = -999;
        vm.AutoScrollResumed += () => resumedFired = true;
        vm.ActiveLineChanged += idx => activeLineReported = idx;

        vm.SetUserScrollingPaused(true);

        // Act
        vm.ResumeAutoScroll();

        // Assert
        resumedFired.Should().BeTrue();
    }

    [Fact]
    public void AutoScrollToggleVisibility_RespectsStaticModeAndLyricsState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        LyricsPayload payload = new()
        {
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "Line 1" }
            }
        };

        vm.AutoScrollToggleVisibility.Should().Be(Visibility.Collapsed);

        // Act - Receive synced lyrics
        client.RaiseLyricsReceived(payload);

        // Assert
        vm.AutoScrollToggleVisibility.Should().Be(Visibility.Visible);

        // Act - Toggle static mode
        vm.ToggleStaticLyricsMode();

        // Assert
        vm.AutoScrollToggleVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void PausedState_AppliesLatencyCompensationExactly()
    {
        // Arrange - paused position math has no wall-clock term, so it is exact
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Timing.LatencyCompensationMs = 200;

        // Act
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t1", Title = "Song", Artist = "Artist", DurationMs = 240000 },
            ProgressMs = 60000,
            IsPlaying = false,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        // Assert
        vm.InterpolatedProgressMs.Should().Be(59800);
    }

    [Fact]
    public void LatencyCompensation_ZeroRemovesTheShiftEntirely()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Timing.LatencyCompensationMs = 0;

        // Act
        client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload { Id = "t1", Title = "Song", Artist = "Artist", DurationMs = 240000 },
            ProgressMs = 60000,
            IsPlaying = false,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        // Assert
        vm.InterpolatedProgressMs.Should().Be(60000);
    }

    [Fact]
    public void AdjustLatencyCompensation_ClampsAndUpdatesCalibrationText()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Timing.LatencyCompensationMs = 200;

        // Act & Assert - steps, clamping at both bounds, and the display text
        vm.Timing.AdjustLatencyCompensation(-50);
        vm.Timing.LatencyCompensationMs.Should().Be(150);
        vm.Timing.CalibrationText.Should().Be("150 ms");

        vm.Timing.AdjustLatencyCompensation(-5000);
        vm.Timing.LatencyCompensationMs.Should().Be(0);

        vm.Timing.AdjustLatencyCompensation(5000);
        vm.Timing.LatencyCompensationMs.Should().Be(1000);
        vm.Timing.CalibrationText.Should().Be("1000 ms");
    }

    [Fact]
    public void PlayingState_CompensationShiftsAnchorByItsValue()
    {
        // Arrange - two view models differing only in compensation; comparing
        // them cancels the wall-clock elapsed term
        PlaybackStatePayload state = new()
        {
            CurrentTrack = new TrackInfoPayload { Id = "t1", Title = "Song", Artist = "Artist", DurationMs = 240000 },
            ProgressMs = 60000,
            IsPlaying = true,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        SignalRPlaybackClient clientA = new();
        LyricsViewModel vmA = new(clientA);
        vmA.Timing.LatencyCompensationMs = 0;

        SignalRPlaybackClient clientB = new();
        LyricsViewModel vmB = new(clientB);
        vmB.Timing.LatencyCompensationMs = 200;

        // Act
        clientA.RaisePlaybackStateReceived(state);
        clientB.RaisePlaybackStateReceived(state);

        // Assert
        long delta = vmA.InterpolatedProgressMs - vmB.InterpolatedProgressMs;
        delta.Should().BeInRange(150, 250);
    }

    [Fact]
    public void InstrumentalBreakVisibility_RequiresBreakWithSyncedLiveLyrics()
    {
        // Arrange - synced lyrics loaded
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new() { TimestampMs = 1000, Text = "first" },
                new() { TimestampMs = 20000, Text = "after the solo" }
            }
        });
        vm.InstrumentalBreakVisibility.Should().Be(Visibility.Collapsed);

        // Act - the tick loop flags a break
        vm.IsInstrumentalBreak = true;
        vm.InstrumentalBreakText = "♪ Instrumental Interlude (12s) ♪";

        // Assert
        vm.InstrumentalBreakVisibility.Should().Be(Visibility.Visible);

        // Act & Assert - static mode hides the indicator even mid-break
        vm.ToggleStaticLyricsMode();
        vm.InstrumentalBreakVisibility.Should().Be(Visibility.Collapsed);
        vm.ToggleStaticLyricsMode();
        vm.InstrumentalBreakVisibility.Should().Be(Visibility.Visible);

        // Act & Assert - break ends
        vm.IsInstrumentalBreak = false;
        vm.InstrumentalBreakVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void InstrumentalBreakVisibility_NotifiesOnEveryDependency()
    {
        // Arrange - the pill binds this property; every dependency change must
        // notify it or the indicator strands (same class of bug as the karaoke
        // toggle's missing notification)
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t1",
            Title = "Song",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload> { new() { TimestampMs = 1000, Text = "line" } }
        });

        List<string> notified = new();
        vm.PropertyChanged += (s, e) => notified.Add(e.PropertyName ?? string.Empty);

        // Act & Assert - break flag flips
        vm.IsInstrumentalBreak = true;
        notified.Should().Contain(nameof(LyricsViewModel.InstrumentalBreakVisibility));

        // Act & Assert - static mode toggles
        notified.Clear();
        vm.ToggleStaticLyricsMode();
        notified.Should().Contain(nameof(LyricsViewModel.InstrumentalBreakVisibility));

        // Act & Assert - lyrics reload
        notified.Clear();
        client.RaiseLyricsReceived(new LyricsPayload
        {
            TrackId = "t2",
            Title = "Next",
            Artist = "Artist",
            IsSynced = true,
            Lines = new List<LyricLinePayload> { new() { TimestampMs = 500, Text = "another" } }
        });
        notified.Should().Contain(nameof(LyricsViewModel.InstrumentalBreakVisibility));
    }

    [Fact]
    public void ThemeChange_ShowsToastWithModeDisplayName()
    {
        // Arrange - default dimensions classify as Large, so the toast uses the stage placement
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Collapsed);
        vm.ThemeToastPageVisibility.Should().Be(Visibility.Collapsed);

        // Act
        vm.SelectedThemeMode = ThemeMode.EmeraldSynth;

        // Assert
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Visible);
        vm.ThemeToastPageVisibility.Should().Be(Visibility.Collapsed);
        vm.ThemeToastText.Should().Be("Emerald Synth");
    }

    [Fact]
    public void ThemeChange_OnSmallBreakpoint_ShowsToastAtPageLevel()
    {
        // Arrange - mobile-sized viewport
        SignalRPlaybackClient client = new();
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(375, 667);
        LyricsViewModel vm = new(client, new ThemeManager(), layout);

        // Act
        vm.SelectedThemeMode = ThemeMode.SolarizedDark;

        // Assert - page overlay on mobile (the lyrics stage may be behind another tab)
        vm.ThemeToastPageVisibility.Should().Be(Visibility.Visible);
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ThemeChange_ViaCycleNextTheme_ShowsToastForEachMode()
    {
        // Arrange - default mode is MidnightViolet, so cycling advances to EmeraldSynth
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());

        // Act & Assert
        vm.Theme.CycleNextTheme();
        vm.ThemeToastText.Should().Be("Emerald Synth");

        vm.Theme.CycleNextTheme();
        vm.ThemeToastText.Should().Be("Cyberpunk Sunset");

        vm.Theme.SetThemeMode(ThemeMode.Dynamic);
        vm.ThemeToastText.Should().Be("Dynamic Palette");
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ThemeToast_AfterHide_CollapsesAgain()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());
        vm.SelectedThemeMode = ThemeMode.NordicSlate;
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Visible);

        // Act - the auto-hide timer invokes this on expiry
        vm.HideThemeToast();

        // Assert
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Collapsed);
        vm.ThemeToastPageVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void SettingSameThemeMode_DoesNotShowToast()
    {
        // Arrange - MidnightViolet is already the default mode
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client, new ThemeManager(), new ResponsiveLayoutManager());

        // Act
        vm.SelectedThemeMode = ThemeMode.MidnightViolet;

        // Assert
        vm.ThemeToastStageVisibility.Should().Be(Visibility.Collapsed);
        vm.ThemeToastPageVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ThemeChange_RaisesPropertyChanged_ForShuffleAndRepeatForegrounds()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        ThemeManager themeManager = new();
        LyricsViewModel vm = new(client, themeManager, new ResponsiveLayoutManager());

        List<string> notified = new();
        vm.Transport.PropertyChanged += (s, e) => notified.Add(e.PropertyName ?? string.Empty);

        // Act
        themeManager.SetThemeMode(ThemeMode.EmeraldSynth);

        // Assert
        notified.Should().Contain(nameof(PlaybackTransport.ShuffleButtonForeground));
        notified.Should().Contain(nameof(PlaybackTransport.RepeatButtonForeground));
    }
}

