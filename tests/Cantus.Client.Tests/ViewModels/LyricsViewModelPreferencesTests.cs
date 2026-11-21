using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
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
            IsPostProcessingEnabled = true,
            LatencyCompensationMs = STORED_LATENCY_MS,
        };

        // Act
        LyricsViewModel vm = CreateViewModel(preferences);

        // Assert
        vm.IsAutoScrollEnabled.Should().BeFalse();
        vm.IsKaraokeModeEnabled.Should().BeFalse();
        vm.IsPostProcessingEnabled.Should().BeTrue();
        vm.PostProcessingVisibility.Should().Be(Visibility.Visible);
        vm.Timing.LatencyCompensationMs.Should().Be(STORED_LATENCY_MS);
        vm.Timing.CalibrationText.Should().Be("400 ms");
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
        vm.Timing.LatencyCompensationMs = UPDATED_LATENCY_MS;
        vm.Timing.AdjustLatencyCompensation(LATENCY_STEP_MS);

        // Assert
        preferences.IsAutoScrollEnabled.Should().BeFalse();
        preferences.IsKaraokeModeEnabled.Should().BeFalse();
        preferences.LatencyCompensationMs.Should().Be(UPDATED_LATENCY_MS + LATENCY_STEP_MS);
    }

    [Fact]
    public void PostProcessing_IsOffByDefault()
    {
        // Act
        LyricsViewModel vm = CreateViewModel(new LyricsPreferences(new InMemorySettingsStore()));

        // Assert
        vm.IsPostProcessingEnabled.Should().BeFalse();
        vm.PostProcessingVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void EnablingPostProcessing_PersistsAndNotifies()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());
        LyricsViewModel vm = CreateViewModel(preferences);
        List<string?> notified = [];
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        vm.IsPostProcessingEnabled = true;

        // Assert
        preferences.IsPostProcessingEnabled.Should().BeTrue();
        vm.PostProcessingVisibility.Should().Be(Visibility.Visible);
        notified.Should().Contain(nameof(LyricsViewModel.IsPostProcessingEnabled));
        notified.Should().Contain(nameof(LyricsViewModel.PostProcessingVisibility));
    }

    [Fact]
    public void SettingPostProcessingToItsCurrentValue_DoesNotNotify()
    {
        // Arrange
        LyricsViewModel vm = CreateViewModel(new LyricsPreferences(new InMemorySettingsStore()));
        List<string?> notified = [];
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        vm.IsPostProcessingEnabled = false;

        // Assert
        notified.Should().NotContain(nameof(LyricsViewModel.IsPostProcessingEnabled));
    }

    [Fact]
    public void OutOfRangeLatency_IsClampedInViewModelAndPreferences()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());
        LyricsViewModel vm = CreateViewModel(preferences);

        // Act
        vm.Timing.AdjustLatencyCompensation(OVERSHOOT_LATENCY_MS);

        // Assert
        vm.Timing.LatencyCompensationMs.Should().Be(MAX_LATENCY_MS);
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
