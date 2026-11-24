using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Services;

public sealed class ThemeManager : INotifyPropertyChanged
{
    private const byte OPAQUE_ALPHA = 255;
    private const byte MAX_CHANNEL = 255;
    private const byte ON_ACCENT_DARK_CHANNEL = 17;
    private const double ON_ACCENT_LUMINANCE_THRESHOLD = 0.5;
    private const double CONTRAST_LUMINANCE_OFFSET = 0.05;
    private const double LUMINANCE_RED_WEIGHT = 0.2126;
    private const double LUMINANCE_GREEN_WEIGHT = 0.7152;
    private const double LUMINANCE_BLUE_WEIGHT = 0.0722;
    private const double SRGB_LINEAR_THRESHOLD = 0.04045;
    private const double SRGB_LINEAR_DIVISOR = 12.92;
    private const double SRGB_GAMMA_OFFSET = 0.055;
    private const double SRGB_GAMMA = 2.4;

    private static readonly ILogger<ThemeManager> _logger = ClientLoggingManager.CreateLogger<ThemeManager>();
    private static ThemeManager? _instance;
    public static ThemeManager Instance =>
        _instance ??= new ThemeManager(AlbumArtColorService.Instance, new ClientPreferences(new LocalSettingsStore()));

    private readonly AlbumArtColorService _artColorService;
    private readonly IClientPreferences? _preferences;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    private ThemeMode _currentMode;
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
    private SolidColorBrush? _onPrimaryAccentBrush;
    private SolidColorBrush? _menuSurfaceBrush;

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

    /// <summary>The artwork service behind the Dynamic theme, shared so the backdrop blur reuses its download.</summary>
    internal AlbumArtColorService ArtColorService => _artColorService;

    /// <summary>Every selectable mode, in the order the theme pickers list them and <see cref="CycleNextTheme"/> walks them.</summary>
    public static IReadOnlyList<ThemeMode> AvailableModes { get; } =
    [
        ThemeMode.Dynamic,
        ThemeMode.MidnightViolet,
        ThemeMode.EmeraldSynth,
        ThemeMode.CyberpunkSunset,
        ThemeMode.NordicSlate,
        ThemeMode.OLEDMonochrome,
        ThemeMode.SolarizedDark,
    ];

    /// <summary>Display names of <see cref="AvailableModes"/>, index for index.</summary>
    public static IReadOnlyList<string> AvailableModeNames { get; } =
        AvailableModes.Select(mode => mode.GetDisplayName()).ToArray();

    public ThemeMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _currentMode = value;
                if (_preferences is not null)
                {
                    _preferences.Theme = value;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentModeIndex));
                ApplyTheme();
            }
        }
    }

    /// <summary>
    /// Position of <see cref="CurrentMode"/> in <see cref="AvailableModes"/>,
    /// for list pickers. Out-of-range writes (a cleared selection is -1) are ignored.
    /// </summary>
    public int CurrentModeIndex
    {
        get => IndexOfMode(_currentMode);
        set
        {
            if (value >= 0 && value < AvailableModes.Count)
            {
                CurrentMode = AvailableModes[value];
            }
        }
    }

    private static int IndexOfMode(ThemeMode mode)
    {
        for (int i = 0; i < AvailableModes.Count; i++)
        {
            if (AvailableModes[i] == mode)
            {
                return i;
            }
        }

        return -1;
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
            OnPropertyChanged(nameof(OnPrimaryAccentBrush));
            OnPropertyChanged(nameof(MenuSurfaceBrush));
            OnPropertyChanged(nameof(ActivePrimaryAccentColor));
            OnPropertyChanged(nameof(ActiveOnPrimaryAccentColor));
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

    /// <summary>Text and icon colour that stays readable on <see cref="PrimaryAccentBrush"/>.</summary>
    public SolidColorBrush OnPrimaryAccentBrush => GetOrCreateBrush(ref _onPrimaryAccentBrush, GetActiveOnAccentColor(ActivePalette));

    /// <summary>Opaque card colour for popups such as menus and dropdowns, which must not show content through.</summary>
    public SolidColorBrush MenuSurfaceBrush => GetOrCreateBrush(ref _menuSurfaceBrush, GetMenuSurfaceColor(ActivePalette));

    public Color ActivePrimaryAccentColor => ActivePalette.PrimaryAccent;
    public Color ActiveOnPrimaryAccentColor => GetActiveOnAccentColor(ActivePalette);
    public Color ActiveBackgroundColor => ActivePalette.Background;

    public event Action<ColorPalette>? PaletteChanged;

    public ThemeManager()
        : this(AlbumArtColorService.Instance)
    {
    }

    public ThemeManager(AlbumArtColorService artColorService)
        : this(artColorService, null)
    {
    }

    /// <summary>
    /// Starts on the saved theme (or <see cref="ClientPreferences.DEFAULT_THEME"/>
    /// without preferences) and saves every later change. The start raises no
    /// change notification, so no theme toast shows at launch.
    /// </summary>
    public ThemeManager(AlbumArtColorService artColorService, IClientPreferences? preferences)
    {
        _artColorService = artColorService;
        _preferences = preferences;
        _currentMode = preferences?.Theme ?? ClientPreferences.DEFAULT_THEME;

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
            if (_onPrimaryAccentBrush is not null) _onPrimaryAccentBrush.Color = GetActiveOnAccentColor(palette);
            if (_menuSurfaceBrush is not null) _menuSurfaceBrush.Color = GetMenuSurfaceColor(palette);
        }
        catch (NotSupportedException)
        {
            // Unit test environment fallback
        }
    }

    // Dynamic accents come from album art, so they pick the label with the higher contrast.
    private Color GetActiveOnAccentColor(ColorPalette palette)
        => GetOnAccentColor(palette.PrimaryAccent, _currentMode == ThemeMode.Dynamic);

    /// <summary>
    /// Near-black or white label colour for <paramref name="accent"/>. With
    /// <paramref name="useHigherContrast"/> it picks whichever has the higher
    /// WCAG contrast ratio. Otherwise only very light accents (OLED Monochrome's
    /// white) get near-black, so the predefined coloured accents keep white labels.
    /// </summary>
    internal static Color GetOnAccentColor(Color accent, bool useHigherContrast = false)
    {
        Color dark = Color.FromArgb(OPAQUE_ALPHA, ON_ACCENT_DARK_CHANNEL, ON_ACCENT_DARK_CHANNEL, ON_ACCENT_DARK_CHANNEL);
        Color white = Color.FromArgb(OPAQUE_ALPHA, MAX_CHANNEL, MAX_CHANNEL, MAX_CHANNEL);
        double accentLuminance = GetRelativeLuminance(accent);

        bool useDark = useHigherContrast
            ? GetContrastRatio(accentLuminance, GetRelativeLuminance(dark)) > GetContrastRatio(accentLuminance, GetRelativeLuminance(white))
            : accentLuminance > ON_ACCENT_LUMINANCE_THRESHOLD;
        return useDark ? dark : white;
    }

    internal static double GetContrastRatio(double luminanceA, double luminanceB)
    {
        double lighter = Math.Max(luminanceA, luminanceB);
        double darker = Math.Min(luminanceA, luminanceB);
        return (lighter + CONTRAST_LUMINANCE_OFFSET) / (darker + CONTRAST_LUMINANCE_OFFSET);
    }

    /// <summary>WCAG relative luminance, from 0 (black) to 1 (white).</summary>
    internal static double GetRelativeLuminance(Color color)
    {
        return (LUMINANCE_RED_WEIGHT * LinearizeChannel(color.R))
            + (LUMINANCE_GREEN_WEIGHT * LinearizeChannel(color.G))
            + (LUMINANCE_BLUE_WEIGHT * LinearizeChannel(color.B));
    }

    private static double LinearizeChannel(byte channel)
    {
        double value = channel / (double)MAX_CHANNEL;
        return value <= SRGB_LINEAR_THRESHOLD
            ? value / SRGB_LINEAR_DIVISOR
            : Math.Pow((value + SRGB_GAMMA_OFFSET) / (1 + SRGB_GAMMA_OFFSET), SRGB_GAMMA);
    }

    internal static Color GetMenuSurfaceColor(ColorPalette palette)
    {
        return Color.FromArgb(OPAQUE_ALPHA, palette.SurfaceCard.R, palette.SurfaceCard.G, palette.SurfaceCard.B);
    }

    public enum ThemeBrush
    {
        Background,
        SurfaceCard,
        MenuSurface,
        CardBorder,
        PrimaryAccent,
        OnPrimaryAccent,
        Glow,
        TextPrimary,
        TextSecondary,
        TextMuted
    }

    /// <summary>
    /// App resource keys that <see cref="ApplyToResources"/> points at the live
    /// theme brushes: the Cantus* keys for views (DataTemplates cannot reach
    /// <c>x:Bind Theme</c>) and the Uno.Material 6.1.1 lightweight-styling keys
    /// read by the TextBox, ComboBox, CheckBox, ProgressRing, Slider and menu templates.
    /// The menu templates read the Material colour brushes directly, so those
    /// are mapped too. Button keys are left alone on purpose: most buttons set
    /// their own translucent Background and Foreground.
    /// </summary>
    internal static IReadOnlyDictionary<string, ThemeBrush> ResourceBrushKeys { get; } = new Dictionary<string, ThemeBrush>
    {
        ["CantusBackgroundBrush"] = ThemeBrush.Background,
        ["CantusSurfaceCardBrush"] = ThemeBrush.SurfaceCard,
        ["CantusMenuSurfaceBrush"] = ThemeBrush.MenuSurface,
        ["CantusCardBorderBrush"] = ThemeBrush.CardBorder,
        ["CantusPrimaryAccentBrush"] = ThemeBrush.PrimaryAccent,
        ["CantusOnPrimaryAccentBrush"] = ThemeBrush.OnPrimaryAccent,
        ["CantusTextPrimaryBrush"] = ThemeBrush.TextPrimary,
        ["CantusTextSecondaryBrush"] = ThemeBrush.TextSecondary,
        ["CantusTextMutedBrush"] = ThemeBrush.TextMuted,

        // Material colour brushes read directly by the MenuFlyout presenter and item templates.
        ["OnSurfaceBrush"] = ThemeBrush.TextPrimary,
        ["OnSurfaceVariantBrush"] = ThemeBrush.TextSecondary,
        ["SurfaceBrush"] = ThemeBrush.MenuSurface,

        ["OutlinedTextBoxForeground"] = ThemeBrush.TextPrimary,
        ["OutlinedTextBoxForegroundPointerOver"] = ThemeBrush.TextPrimary,
        ["OutlinedTextBoxForegroundFocused"] = ThemeBrush.TextPrimary,
        ["OutlinedTextBoxForegroundDisabled"] = ThemeBrush.TextMuted,
        ["OutlinedTextBoxPlaceholderForeground"] = ThemeBrush.TextMuted,
        ["OutlinedTextBoxPlaceholderForegroundPointerOver"] = ThemeBrush.TextSecondary,
        ["OutlinedTextBoxPlaceholderForegroundFocused"] = ThemeBrush.TextSecondary,
        ["OutlinedTextBoxPlaceholderForegroundDisabled"] = ThemeBrush.TextMuted,
        ["OutlinedTextBoxBorderBrush"] = ThemeBrush.CardBorder,
        ["OutlinedTextBoxBorderBrushPointerOver"] = ThemeBrush.TextMuted,
        ["OutlinedTextBoxBorderBrushFocused"] = ThemeBrush.PrimaryAccent,
        ["OutlinedTextBoxBorderBrushDisabled"] = ThemeBrush.CardBorder,
        ["OutlinedTextBoxHeaderForeground"] = ThemeBrush.TextSecondary,
        ["OutlinedTextBoxHeaderForegroundPointerOver"] = ThemeBrush.TextSecondary,
        ["OutlinedTextBoxHeaderForegroundFocused"] = ThemeBrush.PrimaryAccent,
        ["OutlinedTextBoxHeaderForegroundDisabled"] = ThemeBrush.TextMuted,
        ["TextBoxDeleteButtonForeground"] = ThemeBrush.TextSecondary,
        ["TextBoxDeleteButtonForegroundPointerOver"] = ThemeBrush.TextPrimary,
        ["TextBoxDeleteButtonForegroundPressed"] = ThemeBrush.TextPrimary,
        ["TextBoxDeleteButtonForegroundDisabled"] = ThemeBrush.TextMuted,
        ["TextBoxLeadingIconForeground"] = ThemeBrush.TextSecondary,
        ["TextBoxLeadingIconForegroundDisabled"] = ThemeBrush.TextMuted,

        ["ComboBoxBackground"] = ThemeBrush.SurfaceCard,
        ["ComboBoxBackgroundPointerOver"] = ThemeBrush.SurfaceCard,
        ["ComboBoxBackgroundPressed"] = ThemeBrush.SurfaceCard,
        ["ComboBoxBackgroundFocused"] = ThemeBrush.SurfaceCard,
        ["ComboBoxBackgroundFocusedPressed"] = ThemeBrush.SurfaceCard,
        ["ComboBoxBackgroundUnfocused"] = ThemeBrush.SurfaceCard,
        ["ComboBoxBackgroundDisabled"] = ThemeBrush.SurfaceCard,
        ["ComboBoxForeground"] = ThemeBrush.TextPrimary,
        ["ComboBoxForegroundPointerOver"] = ThemeBrush.TextPrimary,
        ["ComboBoxForegroundPressed"] = ThemeBrush.TextPrimary,
        ["ComboBoxForegroundFocused"] = ThemeBrush.TextPrimary,
        ["ComboBoxForegroundFocusedPressed"] = ThemeBrush.TextPrimary,
        ["ComboBoxForegroundDisabled"] = ThemeBrush.TextMuted,
        ["ComboBoxPlaceHolderForeground"] = ThemeBrush.TextMuted,
        ["ComboBoxPlaceHolderForegroundPointerOver"] = ThemeBrush.TextSecondary,
        ["ComboBoxPlaceHolderForegroundPressed"] = ThemeBrush.TextSecondary,
        ["ComboBoxPlaceHolderForegroundFocused"] = ThemeBrush.TextSecondary,
        ["ComboBoxPlaceHolderForegroundFocusedPressed"] = ThemeBrush.TextSecondary,
        ["ComboBoxPlaceHolderForegroundDisabled"] = ThemeBrush.TextMuted,
        ["ComboBoxUpperPlaceHolderForeground"] = ThemeBrush.TextSecondary,
        ["ComboBoxUpperPlaceHolderForegroundPointerOver"] = ThemeBrush.TextSecondary,
        ["ComboBoxUpperPlaceHolderForegroundPressed"] = ThemeBrush.TextSecondary,
        ["ComboBoxUpperPlaceHolderForegroundFocused"] = ThemeBrush.PrimaryAccent,
        ["ComboBoxUpperPlaceHolderForegroundFocusedPressed"] = ThemeBrush.PrimaryAccent,
        ["ComboBoxUpperPlaceHolderForegroundOpened"] = ThemeBrush.PrimaryAccent,
        ["ComboBoxUpperPlaceHolderForegroundDisabled"] = ThemeBrush.TextMuted,
        ["ComboBoxBorderBrush"] = ThemeBrush.CardBorder,
        ["ComboBoxBorderBrushPointerOver"] = ThemeBrush.TextMuted,
        ["ComboBoxBorderBrushPressed"] = ThemeBrush.TextMuted,
        ["ComboBoxBorderBrushOpened"] = ThemeBrush.PrimaryAccent,
        ["ComboBoxBorderBrushDisabled"] = ThemeBrush.CardBorder,
        ["ComboBoxArrowForeground"] = ThemeBrush.TextSecondary,
        ["ComboBoxArrowForegroundOpened"] = ThemeBrush.TextSecondary,
        ["ComboBoxLeadingIconForeground"] = ThemeBrush.TextSecondary,
        ["ComboBoxLeadingIconForegroundDisabled"] = ThemeBrush.TextMuted,
        ["ComboBoxDropDownBackground"] = ThemeBrush.MenuSurface,
        ["ComboBoxDropDownBackgroundPointerOver"] = ThemeBrush.MenuSurface,
        ["ComboBoxDropDownBackgroundPointerPressed"] = ThemeBrush.MenuSurface,
        ["ComboBoxDropDownBackgroundPressed"] = ThemeBrush.MenuSurface,
        ["ComboBoxDropDownBackgroundFocused"] = ThemeBrush.MenuSurface,
        ["ComboBoxDropDownBackgroundFocusedPressed"] = ThemeBrush.MenuSurface,
        ["ComboBoxDropDownBorderBrush"] = ThemeBrush.CardBorder,
        ["ComboBoxItemForeground"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundPointerOver"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundPressed"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundSelected"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundSelectedUnfocused"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundSelectedPointerOver"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundSelectedPressed"] = ThemeBrush.TextPrimary,
        ["ComboBoxItemForegroundDisabled"] = ThemeBrush.TextMuted,
        ["ComboBoxItemForegroundSelectedDisabled"] = ThemeBrush.TextMuted,
        ["ComboBoxItemBackgroundSelected"] = ThemeBrush.Glow,
        ["ComboBoxItemBackgroundSelectedPointerOver"] = ThemeBrush.Glow,
        ["ComboBoxItemBackgroundSelectedPressed"] = ThemeBrush.Glow,

        ["CheckBoxForegroundUnchecked"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundUncheckedPointerOver"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundUncheckedPressed"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundUncheckedDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxForegroundChecked"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundCheckedPointerOver"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundCheckedPressed"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundCheckedDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxForegroundIndeterminate"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundIndeterminatePointerOver"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundIndeterminatePressed"] = ThemeBrush.TextPrimary,
        ["CheckBoxForegroundIndeterminateDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxCheckBackgroundStroke"] = ThemeBrush.TextSecondary,
        ["CheckBoxCheckBackgroundStrokeUnchecked"] = ThemeBrush.TextSecondary,
        ["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = ThemeBrush.TextPrimary,
        ["CheckBoxCheckBackgroundStrokeUncheckedPressed"] = ThemeBrush.TextPrimary,
        ["CheckBoxCheckBackgroundStrokeUncheckedDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxCheckBackgroundStrokeChecked"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundStrokeCheckedPointerOver"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundStrokeCheckedPressed"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundStrokeCheckedDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxCheckBackgroundStrokeIndeterminate"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundStrokeIndeterminatePointerOver"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundStrokeIndeterminatePressed"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundStrokeIndeterminateDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxCheckBackgroundFillChecked"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundFillCheckedPointerOver"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundFillCheckedPressed"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundFillCheckedDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxCheckBackgroundFillIndeterminate"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundFillIndeterminatePointerOver"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundFillIndeterminatePressed"] = ThemeBrush.PrimaryAccent,
        ["CheckBoxCheckBackgroundFillIndeterminateDisabled"] = ThemeBrush.TextMuted,
        ["CheckBoxGlyphForegroundChecked"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundCheckedPointerOver"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundCheckedPressed"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundCheckedDisabled"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundIndeterminate"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundIndeterminatePointerOver"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundIndeterminatePressed"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxGlyphForegroundIndeterminateDisabled"] = ThemeBrush.OnPrimaryAccent,
        ["CheckBoxStateCircleFillUnchecked"] = ThemeBrush.TextPrimary,
        ["CheckBoxStateCircleFillChecked"] = ThemeBrush.PrimaryAccent,

        ["ProgressRingForeground"] = ThemeBrush.PrimaryAccent,

        ["SliderTrackValueFill"] = ThemeBrush.PrimaryAccent,
        ["SliderTrackValueFillDisabled"] = ThemeBrush.TextMuted,
        ["SliderThumbBackground"] = ThemeBrush.PrimaryAccent,
        ["SliderThumbBackgroundDisabled"] = ThemeBrush.TextMuted,
        ["SliderTrackFill"] = ThemeBrush.CardBorder,
        ["SliderTrackFillDisabled"] = ThemeBrush.CardBorder,
    };

    /// <summary>
    /// Points every key in <see cref="ResourceBrushKeys"/> at this manager's
    /// brushes. The brushes keep their identity and only change colour, so
    /// everything that resolves these keys follows later palette changes.
    /// Call it before any view is created: resources are looked up when a
    /// template is applied.
    /// </summary>
    public void ApplyToResources(ResourceDictionary resources)
    {
        foreach (KeyValuePair<string, ThemeBrush> entry in ResourceBrushKeys)
        {
            SolidColorBrush brush = GetBrush(entry.Value);
            if (brush is not null)
            {
                resources[entry.Key] = brush;
            }
        }
    }

    private SolidColorBrush GetBrush(ThemeBrush brush)
    {
        return brush switch
        {
            ThemeBrush.Background => BackgroundBrush,
            ThemeBrush.SurfaceCard => SurfaceCardBrush,
            ThemeBrush.MenuSurface => MenuSurfaceBrush,
            ThemeBrush.CardBorder => CardBorderBrush,
            ThemeBrush.PrimaryAccent => PrimaryAccentBrush,
            ThemeBrush.OnPrimaryAccent => OnPrimaryAccentBrush,
            ThemeBrush.Glow => GlowBrush,
            ThemeBrush.TextPrimary => TextPrimaryBrush,
            ThemeBrush.TextSecondary => TextSecondaryBrush,
            ThemeBrush.TextMuted => TextMutedBrush,
            _ => throw new ArgumentOutOfRangeException(nameof(brush), brush, null)
        };
    }

    public void SetThemeMode(ThemeMode mode)
    {
        CurrentMode = mode;
    }

    public void CycleNextTheme()
    {
        int index = IndexOfMode(CurrentMode);
        CurrentMode = index < 0
            ? ThemeMode.MidnightViolet
            : AvailableModes[(index + 1) % AvailableModes.Count];
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
