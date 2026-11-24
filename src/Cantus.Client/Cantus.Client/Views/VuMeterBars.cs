using System;
using System.Collections.Generic;
using System.ComponentModel;
using Cantus.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Cantus.Client.Views;

/// <summary>
/// Draws <see cref="VuMeterViewModel"/> levels as thin vertical bars behind a
/// seek bar, only over the played part, with the whole spectrum (low to high
/// frequency) spread across that part so it always shows every band. Bars grow out of the seek bar's centre line, and a
/// quiet bar fades to fully transparent (<see cref="VuMeterViewModel.GetBarOpacity"/>),
/// so silent frequencies leave the seek bar clear. Only bars inside the played
/// part are updated, and only when the meter raises
/// <see cref="VuMeterViewModel.LevelsUpdated"/>.
/// </summary>
public sealed partial class VuMeterBars : Canvas
{
    private const double BAR_WIDTH = 3;
    private const double BAR_GAP = 2;
    private const double PITCH = BAR_WIDTH + BAR_GAP;
    private const double MIN_CHANGE = 0.01;

    public static readonly DependencyProperty MeterProperty =
        DependencyProperty.Register(
            nameof(Meter),
            typeof(VuMeterViewModel),
            typeof(VuMeterBars),
            new PropertyMetadata(null, OnMeterChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(VuMeterBars),
            new PropertyMetadata(0.0, (d, e) => ((VuMeterBars)d).UpdatePlayedRegion()));

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(
            nameof(BarBrush),
            typeof(Brush),
            typeof(VuMeterBars),
            new PropertyMetadata(null, (d, e) => ((VuMeterBars)d).ApplyBrush()));

    private readonly List<Rectangle> _bars = [];
    private readonly List<ScaleTransform> _scales = [];
    private double[] _shownOpacity = [];
    private VuMeterViewModel? _subscribed;
    private int _visibleBars;
    private double _clipWidth = -1;
    private bool _isLoaded;

    public VuMeterBars()
    {
        IsHitTestVisible = false;
        SizeChanged += (s, e) => Rebuild();
        Loaded += (s, e) =>
        {
            _isLoaded = true;
            Subscribe(Meter);
        };
        Unloaded += (s, e) =>
        {
            _isLoaded = false;
            Subscribe(null);
        };
    }

    public VuMeterViewModel? Meter
    {
        get => (VuMeterViewModel?)GetValue(MeterProperty);
        set => SetValue(MeterProperty, value);
    }

    /// <summary>The played fraction of the track, 0..1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    private static void OnMeterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VuMeterBars bars = (VuMeterBars)d;
        if (bars._isLoaded)
        {
            bars.Subscribe((VuMeterViewModel?)e.NewValue);
        }
    }

    private void Subscribe(VuMeterViewModel? meter)
    {
        if (_subscribed is not null)
        {
            _subscribed.LevelsUpdated -= OnLevelsUpdated;
            _subscribed.PropertyChanged -= OnMeterPropertyChanged;
        }

        _subscribed = meter;
        if (meter is not null)
        {
            meter.LevelsUpdated += OnLevelsUpdated;
            meter.PropertyChanged += OnMeterPropertyChanged;
        }

        UpdateVisibility();
        ApplyLevels(force: true);
    }

    private void OnMeterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VuMeterViewModel.IsEnabled))
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        Visibility = _subscribed?.IsEnabled == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLevelsUpdated(object? sender, EventArgs e) => ApplyLevels(force: false);

    private void Rebuild()
    {
        Children.Clear();
        _bars.Clear();
        _scales.Clear();
        _visibleBars = 0;
        _clipWidth = -1;

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int count = Math.Max(2, (int)((width + BAR_GAP) / PITCH));
        _shownOpacity = new double[count];
        for (int i = 0; i < count; i++)
        {
            ScaleTransform scale = new() { CenterY = height / 2, ScaleY = 0 };
            Rectangle bar = new()
            {
                Width = BAR_WIDTH,
                Height = height,
                RadiusX = BAR_WIDTH / 2,
                RadiusY = BAR_WIDTH / 2,
                Fill = BarBrush,
                RenderTransform = scale,
                Opacity = 0,
                Visibility = Visibility.Collapsed,
            };
            SetLeft(bar, i * PITCH);
            Children.Add(bar);
            _bars.Add(bar);
            _scales.Add(scale);
        }

        UpdatePlayedRegion();
        ApplyLevels(force: true);
    }

    /// <summary>Shows the bars that start inside the played part and clips the one cut by its edge.</summary>
    private void UpdatePlayedRegion()
    {
        if (_bars.Count == 0)
        {
            return;
        }

        double playedWidth = Math.Clamp(Progress, 0, 1) * ActualWidth;
        int visible = Math.Min(_bars.Count, (int)Math.Ceiling(playedWidth / PITCH));
        if (visible != _visibleBars)
        {
            for (int i = Math.Min(visible, _visibleBars); i < Math.Max(visible, _visibleBars); i++)
            {
                _bars[i].Visibility = i < visible ? Visibility.Visible : Visibility.Collapsed;
            }

            _visibleBars = visible;

            // Every visible bar now maps to a slightly different band.
            ApplyLevels(force: true);
        }

        if (Math.Abs(playedWidth - _clipWidth) >= 0.5)
        {
            _clipWidth = playedWidth;
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, playedWidth, ActualHeight) };
        }
    }

    private void ApplyLevels(bool force)
    {
        if (_scales.Count == 0)
        {
            return;
        }

        double lastIndex = Math.Max(1, _visibleBars - 1);
        for (int i = 0; i < _visibleBars; i++)
        {
            float level = _subscribed?.SampleLevel(i / lastIndex) ?? 0;
            double opacity = VuMeterViewModel.GetBarOpacity(level);
            bool wasHidden = _shownOpacity[i] == 0;
            if (force || Math.Abs(_shownOpacity[i] - opacity) >= MIN_CHANGE || (opacity == 0) != wasHidden)
            {
                _shownOpacity[i] = opacity;
                _bars[i].Opacity = opacity;
            }

            // A transparent bar's height does not show, so it is left alone until it reappears.
            ScaleTransform transform = _scales[i];
            double height = VuMeterViewModel.GetBarHeight(level);
            if (opacity > 0 && (force || wasHidden || Math.Abs(transform.ScaleY - height) >= MIN_CHANGE))
            {
                transform.ScaleY = height;
            }
        }
    }

    private void ApplyBrush()
    {
        foreach (Rectangle bar in _bars)
        {
            bar.Fill = BarBrush;
        }
    }
}
