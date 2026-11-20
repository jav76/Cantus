using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Services;

public sealed class ThemeManager : INotifyPropertyChanged
{
    private static readonly ILogger<ThemeManager> _logger = ClientLoggingManager.CreateLogger<ThemeManager>();
    private static ThemeManager? _instance;
    public static ThemeManager Instance => _instance ??= new ThemeManager();

    private readonly AlbumArtColorService _artColorService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

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

    private CancellationTokenSource? _extractionCts;
    private string? _extractedForUrl;
    private string? _extractionInFlightUrl;

    internal Task? ActiveExtractionTask { get; private set; }

    /// <summary>
    /// URL of the artwork behind the current Dynamic theme, or null when no
    /// artwork-derived theme is active. Used for the full-screen ambient
    /// backdrop. Updated before <see cref="PaletteChanged"/> fires.
    /// </summary>
    public string? AmbientArtworkUrl { get; private set; }

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
            OnPropertyChanged(nameof(ActivePrimaryAccentColor));
            OnPropertyChanged(nameof(ActiveBackgroundColor));
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

    public Color ActivePrimaryAccentColor => ActivePalette.PrimaryAccent;
    public Color ActiveBackgroundColor => ActivePalette.Background;

    public event Action<ColorPalette>? PaletteChanged;

    public ThemeManager()
        : this(AlbumArtColorService.Instance)
    {
    }

    public ThemeManager(AlbumArtColorService artColorService)
    {
        _artColorService = artColorService;

        try
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        }
        catch
        {
            _dispatcherQueue = null;
        }

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
            RefreshDynamicPalette();
        }
    }

    private void ApplyTheme()
    {
        if (CurrentMode == ThemeMode.Dynamic)
        {
            _extractedForUrl = null;
            RefreshDynamicPalette();
            return;
        }

        CancelPendingExtraction();
        _extractedForUrl = null;
        AmbientArtworkUrl = null;

        ActivePalette = CurrentMode switch
        {
            ThemeMode.MidnightViolet => ColorPalette.MidnightViolet,
            ThemeMode.EmeraldSynth => ColorPalette.EmeraldSynth,
            ThemeMode.CyberpunkSunset => ColorPalette.CyberpunkSunset,
            ThemeMode.NordicSlate => ColorPalette.NordicSlate,
            ThemeMode.OLEDMonochrome => ColorPalette.OLEDMonochrome,
            ThemeMode.SolarizedDark => ColorPalette.SolarizedDark,
            _ => ColorPalette.MidnightViolet
        };
    }

    private void RefreshDynamicPalette()
    {
        string? albumArtUrl = _lastAlbumArtUrl;

        if (string.IsNullOrWhiteSpace(albumArtUrl))
        {
            CancelPendingExtraction();
            _extractedForUrl = null;
            AmbientArtworkUrl = null;
            ActivePalette = ColorExtractionHelper.GeneratePaletteFromMetadata(_lastTitle, _lastArtist, albumArtUrl);
            return;
        }

        if (albumArtUrl == _extractedForUrl || albumArtUrl == _extractionInFlightUrl)
        {
            return;
        }

        if (_artColorService.TryGetCachedSwatches(albumArtUrl, out IReadOnlyList<ColorSwatch>? cachedSwatches))
        {
            CancelPendingExtraction();
            _extractedForUrl = albumArtUrl;
            AmbientArtworkUrl = albumArtUrl;
            ActivePalette = ColorExtractionHelper.GeneratePaletteFromSwatches(_lastTitle, cachedSwatches);
            return;
        }

        // Instant metadata-derived fallback while the artwork colors are extracted.
        AmbientArtworkUrl = null;
        ActivePalette = ColorExtractionHelper.GeneratePaletteFromMetadata(_lastTitle, _lastArtist, albumArtUrl);
        BeginExtraction(_lastTitle, albumArtUrl);
    }

    private void BeginExtraction(string? title, string albumArtUrl)
    {
        CancelPendingExtraction();

        CancellationTokenSource extractionCts = new();
        _extractionCts = extractionCts;
        _extractionInFlightUrl = albumArtUrl;
        ActiveExtractionTask = ExtractAndApplyAsync(title, albumArtUrl, extractionCts);
    }

    private async Task ExtractAndApplyAsync(string? title, string albumArtUrl, CancellationTokenSource extractionCts)
    {
        try
        {
            IReadOnlyList<ColorSwatch>? swatches = await _artColorService
                .GetSwatchesAsync(albumArtUrl, extractionCts.Token)
                .ConfigureAwait(false);

            if (swatches is null || swatches.Count == 0)
            {
                return;
            }

            if (ReferenceEquals(_extractionCts, extractionCts) && albumArtUrl == _lastAlbumArtUrl)
            {
                _extractedForUrl = albumArtUrl;
            }

            RunOnUIThread(() =>
            {
                if (!ReferenceEquals(_extractionCts, extractionCts)
                    || albumArtUrl != _lastAlbumArtUrl
                    || CurrentMode != ThemeMode.Dynamic)
                {
                    return;
                }

                AmbientArtworkUrl = albumArtUrl;
                ActivePalette = ColorExtractionHelper.GeneratePaletteFromSwatches(title, swatches);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer track; the fallback palette remains active.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dynamic palette extraction failed");
        }
        finally
        {
            if (ReferenceEquals(_extractionCts, extractionCts))
            {
                _extractionInFlightUrl = null;
            }
        }
    }

    private void CancelPendingExtraction()
    {
        // The source is intentionally not disposed: a superseded extraction task
        // may still observe the token after cancellation.
        _extractionCts?.Cancel();
        _extractionCts = null;
        _extractionInFlightUrl = null;
    }

    private void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue is not null)
        {
            try
            {
                if (!_dispatcherQueue.HasThreadAccess)
                {
                    _dispatcherQueue.TryEnqueue(() => action());
                    return;
                }
            }
            catch
            {
            }
        }
        action();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
