using System;
using Cantus.Client.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace Cantus.Client.Views;

/// <summary>
/// Full-window film grain and vignette drawn over every theme. The grain is a
/// few pre-made noise tiles: each frame shows one tile layer and shifts the
/// whole grid by a random whole-pixel offset, so a frame costs one transform
/// and two opacity changes, never a re-rasterised bitmap. It works the same on
/// the WASM DOM renderer (CSS transforms) and desktop Skia. The frame timer
/// runs only while the overlay is loaded and visible.
/// </summary>
public sealed partial class PostProcessingOverlay : UserControl
{
    private const double GRAIN_TILE_SIZE = 256;
    private const int GRAIN_FRAME_INTERVAL_MS = 42;
    private const double GRAIN_OPACITY = 0.5;
    private const string GRAIN_TILE_URI_FORMAT = "ms-appx:///Assets/PostProcessing/grain_{0}.png";
    private const int GRAIN_TILE_COUNT = 3;

    private const double VIGNETTE_CLEAR_STOP = 0.55;
    private const double VIGNETTE_MID_STOP = 0.82;
    private const byte VIGNETTE_MID_ALPHA = 40;
    private const byte VIGNETTE_EDGE_ALPHA = 110;

    private readonly Random _random = new();
    private readonly Canvas[] _grainLayers = new Canvas[GRAIN_TILE_COUNT];
    private readonly BitmapImage?[] _grainSources = new BitmapImage?[GRAIN_TILE_COUNT];
    private readonly RectangleGeometry _clip = new();

    private DispatcherTimer? _frameTimer;
    private bool _isLoaded;
    private int _currentLayer = -1;
    private int _columns;
    private int _rows;

    public PostProcessingOverlay()
    {
        InitializeComponent();

        VignetteLayer.Background = CreateVignetteBrush();
        GrainHost.Opacity = GRAIN_OPACITY;
        OverlayRoot.Clip = _clip;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityChanged);
    }

    private bool IsActive => _isLoaded && Visibility == Visibility.Visible;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateRunningState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        UpdateRunningState();
    }

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        UpdateRunningState();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        if (IsActive)
        {
            EnsureGrainTiles(e.NewSize.Width, e.NewSize.Height);
        }
    }

    private void UpdateRunningState()
    {
        if (IsActive)
        {
            EnsureGrainTiles(ActualWidth, ActualHeight);
            if (_frameTimer is null)
            {
                _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GRAIN_FRAME_INTERVAL_MS) };
                _frameTimer.Tick += OnFrameTick;
            }

            _frameTimer.Start();
        }
        else
        {
            _frameTimer?.Stop();
        }
    }

    private void OnFrameTick(object? sender, object e)
    {
        int nextLayer = FilmGrainLayout.GetNextLayerIndex(_currentLayer, GRAIN_TILE_COUNT);
        if (_currentLayer >= 0 && _grainLayers[_currentLayer] is Canvas previous)
        {
            previous.Opacity = 0;
        }

        if (_grainLayers[nextLayer] is Canvas next)
        {
            next.Opacity = 1;
        }

        _currentLayer = nextLayer;
        GrainShift.X = FilmGrainLayout.GetJitterOffset(_random.NextDouble(), GRAIN_TILE_SIZE);
        GrainShift.Y = FilmGrainLayout.GetJitterOffset(_random.NextDouble(), GRAIN_TILE_SIZE);
    }

    /// <summary>
    /// Builds the tile grids the first time the overlay is shown, so a device
    /// with effects off never downloads the tiles, and rebuilds them only when
    /// the window grows or shrinks across a tile boundary.
    /// </summary>
    private void EnsureGrainTiles(double width, double height)
    {
        int columns = FilmGrainLayout.GetTileCount(width, GRAIN_TILE_SIZE);
        int rows = FilmGrainLayout.GetTileCount(height, GRAIN_TILE_SIZE);
        if (columns == 0 || rows == 0 || (columns == _columns && rows == _rows))
        {
            return;
        }

        _columns = columns;
        _rows = rows;

        for (int layerIndex = 0; layerIndex < GRAIN_TILE_COUNT; layerIndex++)
        {
            Canvas layer = _grainLayers[layerIndex] ??= CreateLayer(layerIndex == _currentLayer);
            BitmapImage source = _grainSources[layerIndex] ??=
                new BitmapImage(new Uri(string.Format(GRAIN_TILE_URI_FORMAT, layerIndex)));

            layer.Children.Clear();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    Image tile = new()
                    {
                        Source = source,
                        Width = GRAIN_TILE_SIZE,
                        Height = GRAIN_TILE_SIZE,
                        Stretch = Stretch.Fill,
                    };
                    Canvas.SetLeft(tile, column * GRAIN_TILE_SIZE);
                    Canvas.SetTop(tile, row * GRAIN_TILE_SIZE);
                    layer.Children.Add(tile);
                }
            }
        }
    }

    private Canvas CreateLayer(bool isCurrent)
    {
        Canvas layer = new() { Opacity = isCurrent ? 1 : 0 };
        GrainHost.Children.Add(layer);
        return layer;
    }

    private static RadialGradientBrush CreateVignetteBrush()
    {
        RadialGradientBrush brush = new()
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = VIGNETTE_CLEAR_STOP });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(VIGNETTE_MID_ALPHA, 0, 0, 0), Offset = VIGNETTE_MID_STOP });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(VIGNETTE_EDGE_ALPHA, 0, 0, 0), Offset = 1 });
        return brush;
    }
}
