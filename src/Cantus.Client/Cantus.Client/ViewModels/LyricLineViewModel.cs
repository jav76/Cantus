using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Cantus.Client.ViewModels;

public sealed class LyricLineViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    private bool _isPast;
    private double _fontSize = 22.0;
    private FontWeight _fontWeight = FontWeights.Normal;
    private double _opacity = 0.75;

    public long TimestampMs { get; init; }
    public string Text { get; init; } = string.Empty;

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

    private void UpdateVisualProperties()
    {
        if (IsActive)
        {
            FontSize = 32.0;
            FontWeight = FontWeights.Bold;
            Opacity = 1.0;
        }
        else if (IsPast)
        {
            FontSize = 20.0;
            FontWeight = FontWeights.Normal;
            Opacity = 0.45;
        }
        else
        {
            FontSize = 22.0;
            FontWeight = FontWeights.Medium;
            Opacity = 0.75;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
