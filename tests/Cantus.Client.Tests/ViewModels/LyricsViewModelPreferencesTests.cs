using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelPreferencesTests
{
    private const int STORED_LATENCY_MS = 400;
    private const int UPDATED_LATENCY_MS = 650;
    private const int LATENCY_STEP_MS = 50;
    private const int MAX_LATENCY_MS = 1000;
    private const int OVERSHOOT_LATENCY_MS = 5000;

    [Fact]
    public void Constructor_ReadsInitialStateFromInjectedPreferences()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore())
        {
            IsAutoScrollEnabled = false,
            IsKaraokeModeEnabled = false,
            LatencyCompensationMs = STORED_LATENCY_MS,
        };

        // Act
        LyricsViewModel vm = CreateViewModel(preferences);

        // Assert
        vm.IsAutoScrollEnabled.Should().BeFalse();
        vm.IsKaraokeModeEnabled.Should().BeFalse();
        vm.LatencyCompensationMs.Should().Be(STORED_LATENCY_MS);
        vm.CalibrationText.Should().Be("400 ms");
    }

    [Fact]
    public void ChangingPreferences_WritesThroughToInjectedPreferences()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());
        LyricsViewModel vm = CreateViewModel(preferences);

        // Act
        vm.ToggleAutoScroll();
        vm.ToggleKaraokeMode();
        vm.LatencyCompensationMs = UPDATED_LATENCY_MS;
        vm.AdjustLatencyCompensation(LATENCY_STEP_MS);

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeFalse();
        preferences.IsKaraokeModeEnabled.Should().BeFalse();
        preferences.LatencyCompensationMs.Should().Be(UPDATED_LATENCY_MS + LATENCY_STEP_MS);
    }

    [Fact]
    public void OutOfRangeLatency_IsClampedInViewModelAndPreferences()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());
        LyricsViewModel vm = CreateViewModel(preferences);

        // Act
        vm.AdjustLatencyCompensation(OVERSHOOT_LATENCY_MS);

        // Assert
        vm.LatencyCompensationMs.Should().Be(MAX_LATENCY_MS);
        preferences.LatencyCompensationMs.Should().Be(MAX_LATENCY_MS);
    }

    private static LyricsViewModel CreateViewModel(ILyricsPreferences preferences)
    {
        return new LyricsViewModel(
            new SignalRPlaybackClient(),
            new ThemeManager(),
            new ResponsiveLayoutManager(),
            preferences);
    }
}
