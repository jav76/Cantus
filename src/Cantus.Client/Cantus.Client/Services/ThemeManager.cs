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

    private SolidColorBrush? _backgroundBrush;
    private SolidColorBrush? _surfaceCardBrush;
    private SolidColorBrush? _cardBorderBrush;
    private SolidColorBrush? _primaryAccentBrush;
    private SolidColorBrush? _secondaryAccentBrush;
    private SolidColorBrush? _textPrimaryBrush;
    private SolidColorBrush? _textSecondaryBrush;
    private SolidColorBrush? _textMutedBrush;
    private SolidColorBrush? _glowBrush;
    private SolidColorBrush? _activeLyricBrush;
    private SolidColorBrush? _pastLyricBrush;
    private SolidColorBrush? _upcomingLyricBrush;

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
            UpdateBrushes(value);
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

    public SolidColorBrush BackgroundBrush => GetOrCreateBrush(ref _backgroundBrush, ActivePalette.Background);
    public SolidColorBrush SurfaceCardBrush => GetOrCreateBrush(ref _surfaceCardBrush, ActivePalette.SurfaceCard);
    public SolidColorBrush CardBorderBrush => GetOrCreateBrush(ref _cardBorderBrush, ActivePalette.CardBorder);
    public SolidColorBrush PrimaryAccentBrush => GetOrCreateBrush(ref _primaryAccentBrush, ActivePalette.PrimaryAccent);
    public SolidColorBrush SecondaryAccentBrush => GetOrCreateBrush(ref _secondaryAccentBrush, ActivePalette.SecondaryAccent);
    public SolidColorBrush TextPrimaryBrush => GetOrCreateBrush(ref _textPrimaryBrush, ActivePalette.TextPrimary);
    public SolidColorBrush TextSecondaryBrush => GetOrCreateBrush(ref _textSecondaryBrush, ActivePalette.TextSecondary);
    public SolidColorBrush TextMutedBrush => GetOrCreateBrush(ref _textMutedBrush, ActivePalette.TextMuted);
    public SolidColorBrush GlowBrush => GetOrCreateBrush(ref _glowBrush, ActivePalette.GlowColor);
    public SolidColorBrush ActiveLyricBrush => GetOrCreateBrush(ref _activeLyricBrush, ActivePalette.ActiveLyricColor);
    public SolidColorBrush PastLyricBrush => GetOrCreateBrush(ref _pastLyricBrush, ActivePalette.PastLyricColor);
    public SolidColorBrush UpcomingLyricBrush => GetOrCreateBrush(ref _upcomingLyricBrush, ActivePalette.UpcomingLyricColor);

    public event Action<ColorPalette>? PaletteChanged;

    public ThemeManager()
    {
        ApplyTheme();
    }

    private static SolidColorBrush GetOrCreateBrush(ref SolidColorBrush? brush, Color color)
    {
        if (brush is null)
        {
            try
            {
                brush = new SolidColorBrush(color);
            }
            catch (NotSupportedException)
            {
                return null!;
            }
        }
        else
        {
            brush.Color = color;
        }

        return brush;
    }

    private void UpdateBrushes(ColorPalette palette)
    {
        try
        {
            if (_backgroundBrush is not null) _backgroundBrush.Color = palette.Background;
            if (_surfaceCardBrush is not null) _surfaceCardBrush.Color = palette.SurfaceCard;
            if (_cardBorderBrush is not null) _cardBorderBrush.Color = palette.CardBorder;
            if (_primaryAccentBrush is not null) _primaryAccentBrush.Color = palette.PrimaryAccent;
            if (_secondaryAccentBrush is not null) _secondaryAccentBrush.Color = palette.SecondaryAccent;
            if (_textPrimaryBrush is not null) _textPrimaryBrush.Color = palette.TextPrimary;
            if (_textSecondaryBrush is not null) _textSecondaryBrush.Color = palette.TextSecondary;
            if (_textMutedBrush is not null) _textMutedBrush.Color = palette.TextMuted;
            if (_glowBrush is not null) _glowBrush.Color = palette.GlowColor;
            if (_activeLyricBrush is not null) _activeLyricBrush.Color = palette.ActiveLyricColor;
            if (_pastLyricBrush is not null) _pastLyricBrush.Color = palette.PastLyricColor;
            if (_upcomingLyricBrush is not null) _upcomingLyricBrush.Color = palette.UpcomingLyricColor;
        }
        catch (NotSupportedException)
        {
            // Unit test environment fallback
        }
    }

    public void SetThemeMode(ThemeMode mode)
    {
        CurrentMode = mode;
    }

    public void CycleNextTheme()
    {
        ThemeMode nextMode = CurrentMode switch
        {
            ThemeMode.Dynamic => ThemeMode.MidnightViolet,
            ThemeMode.MidnightViolet => ThemeMode.EmeraldSynth,
            ThemeMode.EmeraldSynth => ThemeMode.CyberpunkSunset,
            ThemeMode.CyberpunkSunset => ThemeMode.NordicSlate,
            ThemeMode.NordicSlate => ThemeMode.OLEDMonochrome,
            ThemeMode.OLEDMonochrome => ThemeMode.SolarizedDark,
            ThemeMode.SolarizedDark => ThemeMode.Dynamic,
            _ => ThemeMode.MidnightViolet
        };

        CurrentMode = nextMode;
    }

    public void UpdateTrackMetadata(string? title, string? artist, string? albumArtUrl)
    {
        _lastTitle = title;
        _lastArtist = artist;
        _lastAlbumArtUrl = albumArtUrl;

        if (CurrentMode == ThemeMode.Dynamic)
        {
            ApplyDynamicTheme(title, artist, albumArtUrl);
        }
    }

    private void ApplyTheme()
    {
        ActivePalette = CurrentMode switch
        {
            ThemeMode.MidnightViolet => ColorPalette.MidnightViolet,
            ThemeMode.EmeraldSynth => ColorPalette.EmeraldSynth,
            ThemeMode.CyberpunkSunset => ColorPalette.CyberpunkSunset,
            ThemeMode.NordicSlate => ColorPalette.NordicSlate,
            ThemeMode.OLEDMonochrome => ColorPalette.OLEDMonochrome,
            ThemeMode.SolarizedDark => ColorPalette.SolarizedDark,
            ThemeMode.Dynamic => ColorExtractionHelper.GeneratePaletteFromMetadata(_lastTitle, _lastArtist, _lastAlbumArtUrl),
            _ => ColorPalette.MidnightViolet
        };
    }

    private void ApplyDynamicTheme(string? title, string? artist, string? albumArtUrl)
    {
        ActivePalette = ColorExtractionHelper.GeneratePaletteFromMetadata(title, artist, albumArtUrl);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
