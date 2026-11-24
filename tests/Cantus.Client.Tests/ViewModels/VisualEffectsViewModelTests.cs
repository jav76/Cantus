using System.Collections.Generic;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class VisualEffectsViewModelTests
{
    [Fact]
    public void Constructor_EmptyStore_OnlyTheVuMeterIsOn()
    {
        // Act
        VisualEffectsViewModel effects = new(new LyricsPreferences(new InMemorySettingsStore()));

        // Assert
        foreach (VisualEffect effect in VisualEffectsViewModel.AvailableEffects)
        {
            effects.IsEnabled(effect).Should().Be(effect == VisualEffect.VuMeter);
        }

        effects.VuMeterVisibility.Should().Be(Visibility.Visible);

        effects.FilmGrainVisibility.Should().Be(Visibility.Collapsed);
        effects.VignetteVisibility.Should().Be(Visibility.Collapsed);
        effects.ScanlinesVisibility.Should().Be(Visibility.Collapsed);
        effects.AnyOverlayEffectVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Constructor_ReadsStoredValues()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore())
        {
            IsFilmGrainEnabled = true,
            IsVignetteEnabled = false,
            IsScanlinesEnabled = true,
            IsBackgroundBlurEnabled = true,
        };

        // Act
        VisualEffectsViewModel effects = new(preferences);

        // Assert
        effects.IsFilmGrainEnabled.Should().BeTrue();
        effects.IsVignetteEnabled.Should().BeFalse();
        effects.IsScanlinesEnabled.Should().BeTrue();
        effects.IsBackgroundBlurEnabled.Should().BeTrue();
        effects.FilmGrainVisibility.Should().Be(Visibility.Visible);
        effects.VignetteVisibility.Should().Be(Visibility.Collapsed);
        effects.ScanlinesVisibility.Should().Be(Visibility.Visible);
        effects.AnyOverlayEffectVisibility.Should().Be(Visibility.Visible);
    }

    [Theory]
    [InlineData(VisualEffect.FilmGrain)]
    [InlineData(VisualEffect.Vignette)]
    [InlineData(VisualEffect.Scanlines)]
    [InlineData(VisualEffect.BackgroundBlur)]
    public void SetEnabled_PersistsToTheMatchingPreference(VisualEffect effect)
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());
        VisualEffectsViewModel effects = new(preferences);

        // Act
        effects.SetEnabled(effect, true);

        // Assert
        effects.IsEnabled(effect).Should().BeTrue();
        new VisualEffectsViewModel(preferences).IsEnabled(effect).Should().BeTrue();
        foreach (VisualEffect other in VisualEffectsViewModel.AvailableEffects)
        {
            if (other != effect)
            {
                effects.IsEnabled(other).Should().Be(other == VisualEffect.VuMeter);
            }
        }
    }

    [Theory]
    [InlineData(VisualEffect.FilmGrain, nameof(VisualEffectsViewModel.IsFilmGrainEnabled), nameof(VisualEffectsViewModel.FilmGrainVisibility))]
    [InlineData(VisualEffect.Vignette, nameof(VisualEffectsViewModel.IsVignetteEnabled), nameof(VisualEffectsViewModel.VignetteVisibility))]
    [InlineData(VisualEffect.Scanlines, nameof(VisualEffectsViewModel.IsScanlinesEnabled), nameof(VisualEffectsViewModel.ScanlinesVisibility))]
    public void EnablingAnOverlayEffect_NotifiesItsVisibilityAndTheOverlay(
        VisualEffect effect,
        string enabledName,
        string visibilityName)
    {
        // Arrange
        VisualEffectsViewModel effects = new(new LyricsPreferences(new InMemorySettingsStore()));
        List<string?> notified = [];
        effects.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        effects.SetEnabled(effect, true);

        // Assert
        notified.Should().Contain([enabledName, visibilityName, nameof(VisualEffectsViewModel.AnyOverlayEffectVisibility)]);
        effects.AnyOverlayEffectVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void EnablingBackgroundBlur_NotifiesWithoutShowingTheOverlay()
    {
        // Arrange
        VisualEffectsViewModel effects = new(new LyricsPreferences(new InMemorySettingsStore()));
        List<string?> notified = [];
        effects.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        effects.IsBackgroundBlurEnabled = true;

        // Assert
        notified.Should().Equal(nameof(VisualEffectsViewModel.IsBackgroundBlurEnabled));
        effects.AnyOverlayEffectVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void DisablingTheVuMeter_PersistsAndNotifiesWithoutShowingTheOverlay()
    {
        // Arrange
        LyricsPreferences preferences = new(new InMemorySettingsStore());
        VisualEffectsViewModel effects = new(preferences);
        List<string?> notified = [];
        effects.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        effects.SetEnabled(VisualEffect.VuMeter, false);

        // Assert
        notified.Should().Equal(nameof(VisualEffectsViewModel.IsVuMeterEnabled), nameof(VisualEffectsViewModel.VuMeterVisibility));
        effects.VuMeterVisibility.Should().Be(Visibility.Collapsed);
        preferences.IsVuMeterEnabled.Should().BeFalse();
        effects.AnyOverlayEffectVisibility.Should().Be(Visibility.Collapsed);
    }

    [Theory]
    [InlineData(VisualEffect.FilmGrain, false)]
    [InlineData(VisualEffect.Vignette, false)]
    [InlineData(VisualEffect.Scanlines, false)]
    [InlineData(VisualEffect.BackgroundBlur, false)]
    [InlineData(VisualEffect.VuMeter, true)]
    public void SettingTheCurrentValue_DoesNotNotify(VisualEffect effect, bool defaultValue)
    {
        // Arrange
        VisualEffectsViewModel effects = new(new LyricsPreferences(new InMemorySettingsStore()));
        List<string?> notified = [];
        effects.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        effects.SetEnabled(effect, defaultValue);

        // Assert
        notified.Should().BeEmpty();
    }

    [Fact]
    public void AnyOverlayEffectVisibility_StaysVisibleWhileAnotherLayerIsOn()
    {
        // Arrange
        VisualEffectsViewModel effects = new(new LyricsPreferences(new InMemorySettingsStore()))
        {
            IsFilmGrainEnabled = true,
            IsVignetteEnabled = true,
        };

        // Act
        effects.IsFilmGrainEnabled = false;

        // Assert
        effects.AnyOverlayEffectVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void AvailableEffects_ListsEveryEffectWithADisplayName()
    {
        // Assert
        VisualEffectsViewModel.AvailableEffects.Should().Equal(
            VisualEffect.FilmGrain,
            VisualEffect.Vignette,
            VisualEffect.Scanlines,
            VisualEffect.BackgroundBlur,
            VisualEffect.VuMeter);
        VisualEffect.BackgroundBlur.GetDisplayName().Should().Contain("Dynamic theme");
        VisualEffect.VuMeter.GetDisplayName().Should().Be("Seek bar VU meter");
    }
}
