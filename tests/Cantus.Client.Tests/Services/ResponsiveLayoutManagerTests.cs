using System;
using System.Collections.Generic;
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
    [InlineData(1920.0, 1080.0, LayoutBreakpoint.Large)]
    [InlineData(2560.0, 1440.0, LayoutBreakpoint.Large)]
    [InlineData(3840.0, 2160.0, LayoutBreakpoint.Large)]
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
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);

        // Act - Force Small
        layout.BreakpointOverride = LayoutBreakpoint.Small;

        // Assert
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Small);
        layout.IsSmall.Should().BeTrue();
        layout.ShowMobileTabBar.Should().BeTrue();

        // Act - Reset Override
        layout.BreakpointOverride = null;

        // Assert
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);
    }

    [Fact]
    public void LargeWindow_DoesNotAutoEnterKioskMode()
    {
        // Arrange - fullscreen browser on a 4K monitor
        ResponsiveLayoutManager layout = new();

        // Act
        layout.UpdateDimensions(3840, 2160);

        // Assert - full desktop chrome stays available at any width
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);
        layout.IsFullscreenTv.Should().BeFalse();
        layout.ShowTopHeader.Should().BeTrue();
        layout.HeaderMode.Should().Be(HeaderDisplayMode.Full);
        layout.ShowSidebar.Should().BeTrue();
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
        layout.AlbumArtSize.Should().Be(double.PositiveInfinity);
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

        // 4. FullscreenTv (10-Foot TV, entered via the explicit kiosk toggle)
        layout.IsKioskMode = true;
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

    [Fact]
    public void ToggleKioskMode_EntersAndLeavesFullscreenTv()
    {
        // Arrange
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1280, 800);

        // Act
        layout.ToggleKioskMode();

        // Assert
        layout.IsKioskMode.Should().BeTrue();
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.FullscreenTv);

        // Act
        layout.ToggleKioskMode();

        // Assert
        layout.IsKioskMode.Should().BeFalse();
        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);
    }

    [Fact]
    public void AlbumArtSize_Small_IsUncappedOnEveryTab()
    {
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(375, 667);

        foreach (MobileViewMode view in Enum.GetValues<MobileViewMode>())
        {
            layout.MobileView = view;
            layout.AlbumArtSize.Should().Be(double.PositiveInfinity);
        }
    }
    [Theory]
    [InlineData(680.0, 900.0)]
    [InlineData(768.0, 1024.0)]
    [InlineData(834.0, 1194.0)]
    public void MediumPortrait_KeepsTheSidebar(double width, double height)
    {
        ResponsiveLayoutManager layout = new();

        layout.UpdateDimensions(width, height);

        layout.IsMedium.Should().BeTrue();
        layout.Orientation.Should().Be(LayoutOrientation.Portrait);
        layout.ShowSidebar.Should().BeTrue();
        layout.SidePanelWidth.Should().Be(290.0);
    }

    [Fact]
    public void FullscreenTv_HidesTheSidebar()
    {
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1280, 800);

        layout.IsKioskMode = true;

        layout.ShowSidebar.Should().BeFalse();
    }

    [Theory]
    [InlineData(1080.0, false)]
    [InlineData(1199.0, false)]
    [InlineData(1200.0, true)]
    [InlineData(1600.0, true)]
    public void ShowFullHeaderLabels_FollowsWindowWidth(double width, bool expected)
    {
        ResponsiveLayoutManager layout = new();

        layout.UpdateDimensions(width, 800);

        layout.ShowFullHeaderLabels.Should().Be(expected);
    }

    [Fact]
    public void WidthChangeInsideABreakpoint_NotifiesWidthDependentProperties()
    {
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(1300, 800);
        List<string?> notified = new();
        layout.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        layout.UpdateDimensions(1150, 800);

        layout.CurrentBreakpoint.Should().Be(LayoutBreakpoint.Large);
        layout.ShowFullHeaderLabels.Should().BeFalse();
        notified.Should().Contain(nameof(ResponsiveLayoutManager.ShowFullHeaderLabels));
        notified.Should().Contain(nameof(ResponsiveLayoutManager.IsKioskBarCompact));
        notified.Should().Contain(nameof(ResponsiveLayoutManager.IsKioskBarStacked));
        notified.Should().Contain(nameof(ResponsiveLayoutManager.ContentPadding));
    }

    [Theory]
    [InlineData(320.0, 568.0, true)]
    [InlineData(360.0, 740.0, true)]
    [InlineData(379.0, 800.0, true)]
    [InlineData(390.0, 844.0, false)]
    [InlineData(900.0, 600.0, false)]
    public void IsNarrowPhone_OnlyForSmallScreensUnder380(double width, double height, bool expected)
    {
        ResponsiveLayoutManager layout = new();

        layout.UpdateDimensions(width, height);

        layout.IsNarrowPhone.Should().Be(expected);
    }

    [Theory]
    [InlineData(375.0, true)]
    [InlineData(699.0, true)]
    [InlineData(700.0, false)]
    [InlineData(1920.0, false)]
    public void IsKioskBarCompact_FollowsWindowWidth(double width, bool expected)
    {
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(width, 1000);

        layout.IsKioskMode = true;

        layout.IsKioskBarCompact.Should().Be(expected);
    }

    [Theory]
    [InlineData(320.0, true)]
    [InlineData(479.0, true)]
    [InlineData(480.0, false)]
    [InlineData(1920.0, false)]
    public void IsKioskBarStacked_FollowsWindowWidth(double width, bool expected)
    {
        ResponsiveLayoutManager layout = new();
        layout.UpdateDimensions(width, 1000);

        layout.IsKioskMode = true;

        layout.IsKioskBarStacked.Should().Be(expected);
    }

    [Fact]
    public void KioskContentPadding_ShrinksOnPhoneWidths()
    {
        ResponsiveLayoutManager layout = new();
        layout.IsKioskMode = true;

        layout.UpdateDimensions(1920, 1080);
        layout.ContentPadding.Left.Should().Be(48);

        layout.UpdateDimensions(400, 800);
        layout.ContentPadding.Left.Should().Be(12);
        layout.ContentPadding.Right.Should().Be(12);
    }

    [Fact]
    public void DiagnosticsHudMinWidth_IsDroppedOnSmall()
    {
        ResponsiveLayoutManager layout = new();

        layout.UpdateDimensions(375, 667);
        layout.DiagnosticsHudMinWidth.Should().Be(0.0);

        layout.UpdateDimensions(1280, 800);
        layout.DiagnosticsHudMinWidth.Should().Be(ResponsiveLayoutManager.DIAGNOSTICS_HUD_MIN_WIDTH);
    }

    [Fact]
    public void HeaderAccountNameMaxWidth_IsCapped()
    {
        ResponsiveLayoutManager layout = new();

        layout.HeaderAccountNameMaxWidth.Should().Be(ResponsiveLayoutManager.HEADER_ACCOUNT_NAME_MAX_WIDTH);
    }

    [Theory]
    [InlineData(300.0, false)]
    [InlineData(419.0, false)]
    [InlineData(420.0, true)]
    [InlineData(900.0, true)]
    [InlineData(0.0, true)]
    [InlineData(double.NaN, true)]
    public void ShouldShowStageModeLabels_DependsOnStageWidth(double stageWidth, bool expected)
    {
        ResponsiveLayoutManager.ShouldShowStageModeLabels(stageWidth).Should().Be(expected);
    }
}
