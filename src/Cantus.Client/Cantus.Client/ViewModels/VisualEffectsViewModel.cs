using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Microsoft.UI.Xaml;

namespace Cantus.Client.ViewModels;

/// <summary>
/// The per-device post-processing effects. Grain, vignette and scanlines are
/// overlay layers drawn over every theme; background blur swaps the Dynamic
/// theme's album art backdrop for a blurred copy, and the VU meter draws level
/// bars over the played part of the seek bar. Each toggle persists immediately;
/// all default to off except the VU meter.
/// </summary>
public sealed class VisualEffectsViewModel : INotifyPropertyChanged
{
    public static IReadOnlyList<VisualEffect> AvailableEffects { get; } = Enum.GetValues<VisualEffect>();

    private readonly ILyricsPreferences _preferences;

    private bool _isFilmGrainEnabled;
    private bool _isVignetteEnabled;
    private bool _isScanlinesEnabled;
    private bool _isBackgroundBlurEnabled;
    private bool _isVuMeterEnabled;

    public VisualEffectsViewModel(ILyricsPreferences preferences)
    {
        _preferences = preferences;
        _isFilmGrainEnabled = preferences.IsFilmGrainEnabled;
        _isVignetteEnabled = preferences.IsVignetteEnabled;
        _isScanlinesEnabled = preferences.IsScanlinesEnabled;
        _isBackgroundBlurEnabled = preferences.IsBackgroundBlurEnabled;
        _isVuMeterEnabled = preferences.IsVuMeterEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsFilmGrainEnabled
    {
        get => _isFilmGrainEnabled;
        set
        {
            if (SetOverlayEffect(ref _isFilmGrainEnabled, value, nameof(FilmGrainVisibility)))
            {
                _preferences.IsFilmGrainEnabled = value;
            }
        }
    }

    public bool IsVignetteEnabled
    {
        get => _isVignetteEnabled;
        set
        {
            if (SetOverlayEffect(ref _isVignetteEnabled, value, nameof(VignetteVisibility)))
            {
                _preferences.IsVignetteEnabled = value;
            }
        }
    }

    public bool IsScanlinesEnabled
    {
        get => _isScanlinesEnabled;
        set
        {
            if (SetOverlayEffect(ref _isScanlinesEnabled, value, nameof(ScanlinesVisibility)))
            {
                _preferences.IsScanlinesEnabled = value;
            }
        }
    }

    /// <summary>Only visible in the Dynamic theme, the one theme with an album art backdrop.</summary>
    public bool IsBackgroundBlurEnabled
    {
        get => _isBackgroundBlurEnabled;
        set
        {
            if (_isBackgroundBlurEnabled == value)
            {
                return;
            }

            _isBackgroundBlurEnabled = value;
            _preferences.IsBackgroundBlurEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsVuMeterEnabled
    {
        get => _isVuMeterEnabled;
        set
        {
            if (_isVuMeterEnabled == value)
            {
                return;
            }

            _isVuMeterEnabled = value;
            _preferences.IsVuMeterEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VuMeterVisibility));
        }
    }

    public Visibility VuMeterVisibility => ToVisibility(_isVuMeterEnabled);

    public Visibility FilmGrainVisibility => ToVisibility(_isFilmGrainEnabled);

    public Visibility VignetteVisibility => ToVisibility(_isVignetteEnabled);

    public Visibility ScanlinesVisibility => ToVisibility(_isScanlinesEnabled);

    /// <summary>Collapses the whole overlay when no overlay layer is on.</summary>
    public Visibility AnyOverlayEffectVisibility =>
        ToVisibility(_isFilmGrainEnabled || _isVignetteEnabled || _isScanlinesEnabled);

    public bool IsEnabled(VisualEffect effect) => effect switch
    {
        VisualEffect.FilmGrain => IsFilmGrainEnabled,
        VisualEffect.Vignette => IsVignetteEnabled,
        VisualEffect.Scanlines => IsScanlinesEnabled,
        VisualEffect.BackgroundBlur => IsBackgroundBlurEnabled,
        VisualEffect.VuMeter => IsVuMeterEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null),
    };

    public void SetEnabled(VisualEffect effect, bool isEnabled)
    {
        switch (effect)
        {
            case VisualEffect.FilmGrain:
                IsFilmGrainEnabled = isEnabled;
                break;
            case VisualEffect.Vignette:
                IsVignetteEnabled = isEnabled;
                break;
            case VisualEffect.Scanlines:
                IsScanlinesEnabled = isEnabled;
                break;
            case VisualEffect.BackgroundBlur:
                IsBackgroundBlurEnabled = isEnabled;
                break;
            case VisualEffect.VuMeter:
                IsVuMeterEnabled = isEnabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
        }
    }

    private bool SetOverlayEffect(
        ref bool field,
        bool value,
        string visibilityName,
        [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(visibilityName);
        OnPropertyChanged(nameof(AnyOverlayEffectVisibility));
        return true;
    }

    private static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
