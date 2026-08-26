using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Cantus.Core.Models;
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

    private bool _isActive;
    private bool _isPast;
    private bool _isCalibrationMode;
    private double _activeFontSize = 32.0;
    private double _inactiveFontSize = 22.0;
    private double _pastFontSize = 20.0;
    private double _fontSize = 22.0;
    private FontWeight _fontWeight = FontWeights.Normal;
    private double _opacity = 0.75;
    private IReadOnlyList<LyricWordViewModel> _words = [];

    public long TimestampMs { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<LyricSyllable>? Syllables { get; init; }

    public IReadOnlyList<LyricWordViewModel> Words
    {
        get => _words;
        private set
        {
            _words = value;
            OnPropertyChanged();
        }
    }

    public SolidColorBrush LineBrush => IsActive ? ActiveBrush : (IsPast ? PastBrush : InactiveBrush);
    public TextAlignment Alignment => TextAlignment.Center;

    public bool IsCalibrationMode
    {
        get => _isCalibrationMode;
        set
        {
            if (_isCalibrationMode != value)
            {
                _isCalibrationMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationBadgeVisibility));
            }
        }
    }

    public Visibility CalibrationBadgeVisibility => _isCalibrationMode ? Visibility.Visible : Visibility.Collapsed;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
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

    public event Action<LyricLineViewModel>? LineClicked;
    public event Action<LyricWordViewModel>? WordClicked;

    public void OnLineClicked() => LineClicked?.Invoke(this);
    public void OnWordClicked(LyricWordViewModel word) => WordClicked?.Invoke(word);

    public void PopulateWords(TimeSpan? lineDuration = null)
    {
        var lineModel = new LyricLine(TimeSpan.FromMilliseconds(TimestampMs), Text, Syllables);
        var wordTimestamps = lineModel.GetWordTimestamps(lineDuration);

        var list = new List<LyricWordViewModel>(wordTimestamps.Count);
        foreach (var wt in wordTimestamps)
        {
            list.Add(new LyricWordViewModel(wt.Word, (long)wt.Timestamp.TotalMilliseconds, wt.WordIndex, this));
        }

        Words = list;
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

