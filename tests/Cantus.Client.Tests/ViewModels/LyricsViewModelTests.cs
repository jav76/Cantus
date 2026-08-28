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

        // Act - Switch to FullscreenTv
        layout.UpdateDimensions(1920, 1080);

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
        vm.OffsetText.Should().Be("+0.0s");

        // Act - without playing track, nudge is safe no-op
        await vm.NudgeOffsetAsync(500);

        // Assert
        vm.OffsetText.Should().Be("+0.0s");
    }

    [Fact]
    public async Task ResetOffsetAsync_ResetsOffsetDisplay()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);

        // Act
        await vm.ResetOffsetAsync();

        // Assert
        vm.OffsetText.Should().Be("+0.0s");
    }

    [Fact]
    public async Task LogoutAsync_ResetsSessionAndPlaybackState()
    {
        // Arrange
        SignalRPlaybackClient client = new();
        LyricsViewModel vm = new(client);
        vm.Sessions.Add(new AuthorizedSessionPayload
        {
            Id = "user-1",
            DisplayName = "Test User"
        });
        vm.AuthorizedSessionsCount = 1;
        vm.ActiveUserId = "user-1";
        vm.ActiveUserName = "Test User";
        vm.CurrentTitle = "Playing Song";
        vm.IsPlaying = true;

        vm.CurrentUserSession.Should().NotBeNull();
        vm.IsAuthorized.Should().BeTrue();

        // Act
        await vm.LogoutAsync();

        // Assert
        vm.Sessions.Should().BeEmpty();
        vm.CurrentUserSession.Should().BeNull();
        vm.AuthorizedSessionsCount.Should().Be(0);
        vm.ActiveUserId.Should().BeNull();
        vm.ActiveUserName.Should().Be("None");
        vm.IsAuthorized.Should().BeFalse();
        vm.CurrentTitle.Should().Be("No Track Playing");
        vm.IsPlaying.Should().BeFalse();
        vm.HasLyrics.Should().BeFalse();
        vm.IsStaticLyricsMode.Should().BeFalse();
        vm.LyricLines.Should().BeEmpty();
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
}

