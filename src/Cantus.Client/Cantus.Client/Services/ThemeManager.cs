using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Client.Models;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Services;

public sealed class ThemeManager : INotifyPropertyChanged
{
    private static ThemeManager? _instance;
    public static ThemeManager Instance => _instance ??= new ThemeManager();

    private ThemeMode _currentMode = ThemeMode.MidnightViolet;
    private ColorPalette _activePalette = ColorPalette.MidnightViolet;

    private string? _lastTitle;
    private string? _lastArtist;
    private string? _lastAlbumArtUrl;

    public ThemeMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _currentMode = value;
                OnPropertyChanged();
                ApplyTheme();
            }
        }
    }

    public ColorPalette ActivePalette
    {
        get => _activePalette;
        private set
        {
            _activePalette = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundBrush));
            OnPropertyChanged(nameof(SurfaceCardBrush));
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(PrimaryAccentBrush));
            OnPropertyChanged(nameof(SecondaryAccentBrush));
            OnPropertyChanged(nameof(TextPrimaryBrush));
            OnPropertyChanged(nameof(TextSecondaryBrush));
            OnPropertyChanged(nameof(TextMutedBrush));
            OnPropertyChanged(nameof(GlowBrush));
            OnPropertyChanged(nameof(ActiveLyricBrush));
            OnPropertyChanged(nameof(PastLyricBrush));
            OnPropertyChanged(nameof(UpcomingLyricBrush));
            PaletteChanged?.Invoke(value);
        }
    }

    public SolidColorBrush BackgroundBrush => new(ActivePalette.Background);
    public SolidColorBrush SurfaceCardBrush => new(ActivePalette.SurfaceCard);
    public SolidColorBrush CardBorderBrush => new(ActivePalette.CardBorder);
    public SolidColorBrush PrimaryAccentBrush => new(ActivePalette.PrimaryAccent);
    public SolidColorBrush SecondaryAccentBrush => new(ActivePalette.SecondaryAccent);
    public SolidColorBrush TextPrimaryBrush => new(ActivePalette.TextPrimary);
    public SolidColorBrush TextSecondaryBrush => new(ActivePalette.TextSecondary);
    public SolidColorBrush TextMutedBrush => new(ActivePalette.TextMuted);
    public SolidColorBrush GlowBrush => new(ActivePalette.GlowColor);
    public SolidColorBrush ActiveLyricBrush => new(ActivePalette.ActiveLyricColor);
    public SolidColorBrush PastLyricBrush => new(ActivePalette.PastLyricColor);
    public SolidColorBrush UpcomingLyricBrush => new(ActivePalette.UpcomingLyricColor);

    public event Action<ColorPalette>? PaletteChanged;

    public ThemeManager()
    {
        ApplyTheme();
    }

    public void SetThemeMode(ThemeMode mode)
    {
        CurrentMode = mode;
    }

    public void CycleNextTheme()
    {
        ThemeMode[] modes = (ThemeMode[])Enum.GetValues(typeof(ThemeMode));
        int nextIndex = ((int)CurrentMode + 1) % modes.Length;
        SetThemeMode(modes[nextIndex]);
    }

    public void UpdateTrackMetadata(string? title, string? artist, string? albumArtUrl)
    {
        _lastTitle = title;
        _lastArtist = artist;
        _lastAlbumArtUrl = albumArtUrl;

        if (CurrentMode == ThemeMode.Dynamic)
        {
            ActivePalette = ColorExtractionHelper.GeneratePaletteFromMetadata(
                title,
                artist,
                albumArtUrl);
        }
    }

    private void ApplyTheme()
    {
        if (CurrentMode == ThemeMode.Dynamic)
        {
            ActivePalette = ColorExtractionHelper.GeneratePaletteFromMetadata(
                _lastTitle,
                _lastArtist,
                _lastAlbumArtUrl);
        }
        else
        {
            ActivePalette = ColorPalette.GetPredefined(CurrentMode);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
