using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Cantus.Core.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelTests
{
    [Fact]
    public void FindActiveLineIndex_WithNoLines_ReturnsMinusOne()
    {
        // Arrange
        var client = new SignalRPlaybackClient();
        var vm = new LyricsViewModel(client);

        // Act
        int result = vm.FindActiveLineIndex(15000);

        // Assert
        result.Should().Be(-1);
    }

    [Fact]
    public void FindActiveLineIndex_WithMultipleLines_CorrectlyLocatesLines()
    {
        // Arrange
        var client = new SignalRPlaybackClient();
        var vm = new LyricsViewModel(client);

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
        var line = new LyricLineViewModel { TimestampMs = 5000, Text = "Hello world" };

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
    public void ToggleKioskMode_TogglesState()
    {
        // Arrange
        var client = new SignalRPlaybackClient();
        var vm = new LyricsViewModel(client);
        vm.IsKioskMode.Should().BeFalse();

        // Act
        vm.ToggleKioskMode();

        // Assert
        vm.IsKioskMode.Should().BeTrue();

        // Act
        vm.ToggleKioskMode();

        // Assert
        vm.IsKioskMode.Should().BeFalse();
    }
}
