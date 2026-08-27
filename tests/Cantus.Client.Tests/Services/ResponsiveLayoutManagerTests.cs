using System;
using Cantus.Client.Models;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ResponsiveLayoutManagerTests
{
    [Theory]
    [InlineData(320.0, 568.0, LayoutBreakpoint.Small)]
    [InlineData(375.0, 667.0, LayoutBreakpoint.Small)]
    [InlineData(480.0, 800.0, LayoutBreakpoint.Small)]
    [InlineData(600.0, 960.0, LayoutBreakpoint.Small)]
    [InlineData(679.0, 800.0, LayoutBreakpoint.Small)]
    [InlineData(680.0, 900.0, LayoutBreakpoint.Medium)]
    [InlineData(768.0, 1024.0, LayoutBreakpoint.Medium)]
    [InlineData(834.0, 1194.0, LayoutBreakpoint.Medium)]
    [InlineData(1024.0, 768.0, LayoutBreakpoint.Medium)]
    [InlineData(1079.0, 800.0, LayoutBreakpoint.Medium)]
    [InlineData(1080.0, 720.0, LayoutBreakpoint.Large)]
    [InlineData(1280.0, 800.0, LayoutBreakpoint.Large)]
    [InlineData(1440.0, 900.0, LayoutBreakpoint.Large)]
    [InlineData(1600.0, 1050.0, LayoutBreakpoint.Large)]
    [InlineData(1919.0, 1080.0, LayoutBreakpoint.Large)]
    [InlineData(1920.0, 1080.0, LayoutBreakpoint.FullscreenTv)]
    [InlineData(2560.0, 1440.0, LayoutBreakpoint.FullscreenTv)]
    [InlineData(3840.0, 2160.0, LayoutBreakpoint.FullscreenTv)]
    public void UpdateDimensions_ClassifiesBreakpointsAccurately(
        double width,
        double height,
        LayoutBreakpoint expectedBreakpoint)
    {
        // Arrange
        ResponsiveLayoutManager layout = new();

        // Act
        layout.UpdateDimensions(width, height);

        // Assert
        layout.CurrentBreakpoint.Should().Be(expectedBreakpoint);
        layout.WindowWidth.Should().Be(width);
        layout.WindowHeight.Should().Be(height);
    }

    [Fact]
    public void KioskMode_OverridesBreakpointToFullscreenTv()
    {
        // Arrange
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1280, 800);
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);

        // Act
        layout.IsKioskMode = true;

        // Assert
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.FullscreenTv);
        layout.IsFullscreenTv.Should().BeTrue();
        layout.ShowMiniBottomBar.Should().BeTrue();
        layout.ShowTopHeader.Should().BeFalse();

        // Act - Exit Kiosk Mode
        layout.IsKioskMode = false;

        // Assert
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);
        layout.IsLarge.Should().BeTrue();
        layout.ShowTopHeader.Should().BeTrue();
    }

    [Fact]
    public void BreakpointOverride_TakesPrecedenceOverWindowDimensions()
    {
        // Arrange
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1920, 1080);
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.FullscreenTv);

        // Act - Force Small
        layout.BreakpointOverride = LayoutBreakpoint.Small;

        // Assert
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Small);
        layout.IsSmall.Should().BeTrue();
        layout.ShowMobileTabBar.Should().BeTrue();

        // Act - Reset Override
        layout.BreakpointOverride = null;

        // Assert
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.FullscreenTv);
    }

    [Theory]
    [InlineData(1280, 800, LayoutOrientation.Landscape)]
    [InlineData(800, 1280, LayoutOrientation.Portrait)]
    [InlineData(375, 667, LayoutOrientation.Portrait)]
    [InlineData(667, 375, LayoutOrientation.Landscape)]
    [InlineData(1000, 1000, LayoutOrientation.Landscape)]
    public void Orientation_CalculatesCorrectly(double width, double height, LayoutOrientation expectedOrientation)
    {
        // Arrange
        ResponsiveLayoutManager layout = new();

        // Act
        layout.UpdateDimensions(width, height);

        // Assert
        layout.Orientation.Should().Be(expectedOrientation);
    }

    [Fact]
    public void AdaptiveProperties_ScaleAcrossAllBreakpoints()
    {
        ResponsiveLayoutManager layout = new();

        // 1. Small (Mobile)
        layout.UpdateDimensions(375, 667);
        layout.IsSmall.Should().BeTrue();
        layout.IsCompact.Should().BeTrue();
        layout.SidePanelWidth.Should().Be(0.0);
        layout.AlbumArtSize.Should().Be(64.0);
        layout.ActiveLyricsFontSize.Should().Be(24.0);
        layout.InactiveLyricsFontSize.Should().Be(16.0);
        layout.PastLyricsFontSize.Should().Be(15.0);
        layout.HeaderMode.Should().Be(HeaderDisplayMode.Minimal);
        layout.ShowMobileTabBar.Should().BeTrue();
        layout.ShowSidebar.Should().BeFalse();

        // 2. Medium (Tablet Landscape)
        layout.UpdateDimensions(900, 600);
        layout.IsMedium.Should().BeTrue();
        layout.SidePanelWidth.Should().Be(290.0);
        layout.AlbumArtSize.Should().Be(230.0);
        layout.ActiveLyricsFontSize.Should().Be(32.0);
        layout.InactiveLyricsFontSize.Should().Be(20.0);
        layout.HeaderMode.Should().Be(HeaderDisplayMode.Compact);
        layout.ShowSidebar.Should().BeTrue();
        layout.ShowMobileTabBar.Should().BeFalse();

        // 3. Large (Desktop)
        layout.UpdateDimensions(1440, 900);
        layout.IsLarge.Should().BeTrue();
        layout.IsWide.Should().BeTrue();
        layout.SidePanelWidth.Should().Be(380.0);
        layout.AlbumArtSize.Should().Be(332.0);
        layout.ActiveLyricsFontSize.Should().Be(38.0);
        layout.InactiveLyricsFontSize.Should().Be(23.0);
        layout.HeaderMode.Should().Be(HeaderDisplayMode.Full);
        layout.ShowSidebar.Should().BeTrue();

        // 4. FullscreenTv (10-Foot TV)
        layout.UpdateDimensions(1920, 1080);
        layout.IsFullscreenTv.Should().BeTrue();
        layout.SidePanelWidth.Should().Be(0.0);
        layout.ActiveLyricsFontSize.Should().Be(50.0);
        layout.InactiveLyricsFontSize.Should().Be(30.0);
        layout.LyricsMaxWidth.Should().Be(1100.0);
        layout.HeaderMode.Should().Be(HeaderDisplayMode.Hidden);
        layout.ShowMiniBottomBar.Should().BeTrue();
    }

    [Fact]
    public void MobileViewMode_Cycling_AdvancesThroughTabsCorrectly()
    {
        // Arrange
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(375, 667);
        layout.MobileView.Should().Be(MobileViewMode.Lyrics);
        layout.IsMobileLyricsActive.Should().BeTrue();
        layout.IsMobileNowPlayingActive.Should().BeFalse();
        layout.IsMobileSettingsActive.Should().BeFalse();

        // Act 1: Cycle to Now Playing
        layout.CycleMobileView();
        layout.MobileView.Should().Be(MobileViewMode.NowPlaying);
        layout.IsMobileLyricsActive.Should().BeFalse();
        layout.IsMobileNowPlayingActive.Should().BeTrue();
        layout.IsMobileSettingsActive.Should().BeFalse();
        layout.AlbumArtSize.Should().Be(240.0);

        // Act 2: Cycle to Settings
        layout.CycleMobileView();
        layout.MobileView.Should().Be(MobileViewMode.SyncAndSettings);
        layout.IsMobileLyricsActive.Should().BeFalse();
        layout.IsMobileNowPlayingActive.Should().BeFalse();
        layout.IsMobileSettingsActive.Should().BeTrue();

        // Act 3: Cycle back to Lyrics
        layout.CycleMobileView();
        layout.MobileView.Should().Be(MobileViewMode.Lyrics);
        layout.IsMobileLyricsActive.Should().BeTrue();
    }

    [Fact]
    public void BreakpointChanged_FiresOnBreakpointTransition()
    {
        // Arrange
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1440, 900);

        LayoutBreakpoint? receivedBreakpoint = null;
        layout.BreakpointChanged += bp => receivedBreakpoint = bp;

        // Act
        layout.UpdateDimensions(500, 800);

        // Assert
        receivedBreakpoint.Should().Be(LayoutBreakpoint.Small);
    }
}
