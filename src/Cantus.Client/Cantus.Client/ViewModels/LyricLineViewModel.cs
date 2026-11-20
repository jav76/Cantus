using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Client.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Cantus.Client.ViewModels;

public sealed class LyricLineViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush ActiveBrush = new(Windows.UI.Color.FromArgb(255, 248, 250, 252));
    private static readonly SolidColorBrush PastBrush = new(Windows.UI.Color.FromArgb(120, 148, 163, 184));
    private static readonly SolidColorBrush InactiveBrush = new(Windows.UI.Color.FromArgb(200, 203, 213, 225));
    private static readonly SolidColorBrush ProgressBrush = new(Windows.UI.Color.FromArgb(255, 192, 132, 252));

    private const double PROGRESS_RENDER_EPSILON = 0.003;

    private bool _isActive;
    private bool _isPast;
    private double _activeFontSize = 32.0;
    private double _inactiveFontSize = 22.0;
    private double _pastFontSize = 20.0;
    private double _fontSize = 22.0;
    private FontWeight _fontWeight = FontWeights.Normal;
    private double _opacity = 0.75;
    private bool _isKaraokeEnabled = true;
    private readonly ScaleTransform? _lineProgressTransform;
    private readonly IReadOnlyList<SyllableTiming>? _syllables;
    private readonly SyllableFillTimeline? _syllableFill;

    public LyricLineViewModel()
    {
        try
        {
            _lineProgressTransform = new ScaleTransform { ScaleX = 0.0, ScaleY = 1.0 };
        }
        catch (NotSupportedException)
        {
            // Headless test environments cannot create XAML transforms.
            _lineProgressTransform = null;
        }
    }

    public long TimestampMs { get; init; }
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The line's real duration (gap to the next line's timestamp), or null for
    /// the final line, where no honest end time exists and the progress
    /// underline stays hidden.
    /// </summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Real per-syllable timing, or null when the line only has line-level timing. When it holds
    /// visible text the fill follows it instead of <see cref="DurationMs"/>.
    /// </summary>
    public IReadOnlyList<SyllableTiming>? Syllables
    {
        get => _syllables;
        init
        {
            _syllables = value;
            _syllableFill = SyllableFillTimeline.Create(value);
        }
    }

    /// <summary>True when the fill is driven by real syllable timing.</summary>
    public bool HasWordTiming => _syllableFill is not null;

    public SolidColorBrush LineBrush => IsActive ? ActiveBrush : (IsPast ? PastBrush : InactiveBrush);
    public SolidColorBrush LineProgressBrush => ProgressBrush;
    public TextAlignment Alignment => TextAlignment.Center;

    /// <summary>
    /// Elapsed fraction of the active line's duration, 0..1. Mirrors the render
    /// transform so the value is assertable in headless tests.
    /// </summary>
    public double LineProgressFraction { get; private set; }

    /// <summary>
    /// Scale transform driving the progress underline's width. Mutated in place
    /// each tick (no per-frame INPC churn), matching the app's mutate-in-place
    /// brush pattern. Null only in headless test environments.
    /// </summary>
    public ScaleTransform? LineProgressTransform => _lineProgressTransform;

    public Visibility LineProgressVisibility =>
        IsActive && (HasWordTiming || DurationMs.HasValue) && _isKaraokeEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// Pushed down from the view model's persisted karaoke-mode toggle so the
    /// per-line visibility stays one x:Bind level deep.
    /// </summary>
    public void SetKaraokeEnabled(bool enabled)
    {
        if (_isKaraokeEnabled != enabled)
        {
            _isKaraokeEnabled = enabled;
            if (!enabled)
            {
                ResetLineProgress();
            }

            OnPropertyChanged(nameof(LineProgressVisibility));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                if (!value)
                {
                    ResetLineProgress();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(LineProgressVisibility));
                UpdateVisualProperties();
            }
        }
    }

    public bool IsPast
    {
        get => _isPast;
        set
        {
            if (_isPast != value)
            {
                _isPast = value;
                OnPropertyChanged();
                UpdateVisualProperties();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        private set
        {
            if (Math.Abs(_fontSize - value) > 0.1)
            {
                _fontSize = value;
                OnPropertyChanged();
            }
        }
    }

    public FontWeight FontWeight
    {
        get => _fontWeight;
        private set
        {
            if (_fontWeight.Weight != value.Weight)
            {
                _fontWeight = value;
                OnPropertyChanged();
            }
        }
    }

    public double Opacity
    {
        get => _opacity;
        private set
        {
            if (Math.Abs(_opacity - value) > 0.01)
            {
                _opacity = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Advances the progress underline. With syllable timing it reaches the
    /// sung word, holding still through gaps between syllables. Without it
    /// only the line's start and end times are real data, so the underline
    /// claims elapsed time over the line's duration - never a specific word.
    /// </summary>
    public void UpdateLineProgress(long positionMs)
    {
        if (!IsActive)
        {
            return;
        }

        double fraction;
        if (_syllableFill is not null)
        {
            fraction = _syllableFill.ComputeFraction(positionMs);
        }
        else if (DurationMs is > 0)
        {
            fraction = Math.Clamp((positionMs - TimestampMs) / (double)DurationMs.Value, 0.0, 1.0);
        }
        else
        {
            return;
        }

        if (Math.Abs(fraction - LineProgressFraction) < PROGRESS_RENDER_EPSILON && fraction is not (0.0 or 1.0))
        {
            return;
        }

        LineProgressFraction = fraction;
        if (_lineProgressTransform is not null)
        {
            _lineProgressTransform.ScaleX = fraction;
        }
    }

    private void ResetLineProgress()
    {
        LineProgressFraction = 0.0;
        if (_lineProgressTransform is not null)
        {
            _lineProgressTransform.ScaleX = 0.0;
        }
    }

    public void RefreshFontSizes(double activeSize, double inactiveSize, double pastSize)
    {
        _activeFontSize = activeSize;
        _inactiveFontSize = inactiveSize;
        _pastFontSize = pastSize;
        UpdateVisualProperties();
    }

    private void UpdateVisualProperties()
    {
        if (IsActive)
        {
            FontSize = _activeFontSize;
            FontWeight = FontWeights.Bold;
            Opacity = 1.0;
        }
        else if (IsPast)
        {
            FontSize = _pastFontSize;
            FontWeight = FontWeights.Normal;
            Opacity = 0.45;
        }
        else
        {
            FontSize = _inactiveFontSize;
            FontWeight = FontWeights.Medium;
            Opacity = 0.75;
        }
        OnPropertyChanged(nameof(LineBrush));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
