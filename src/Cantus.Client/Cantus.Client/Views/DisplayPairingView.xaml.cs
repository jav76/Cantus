using System.ComponentModel;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Cantus.Client.Views;

/// <summary>
/// The pairing screen an unpaired client shows while it waits for an owner to
/// approve its code: the code itself, read across a room, and a QR of the approval link.
/// </summary>
public sealed partial class DisplayPairingView : UserControl
{
    private const double QR_MODULE_SIZE = 8.0;

    // Rows overlap by a fraction of a pixel so scaled rendering leaves no seams between them.
    private const double QR_ROW_OVERLAP = 0.5;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(LyricsViewModel),
            typeof(DisplayPairingView),
            new PropertyMetadata(null, OnViewModelChanged));

    public LyricsViewModel? ViewModel
    {
        get => (LyricsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty PairingProperty =
        DependencyProperty.Register(
            nameof(Pairing),
            typeof(DisplayPairingViewModel),
            typeof(DisplayPairingView),
            new PropertyMetadata(null));

    public DisplayPairingViewModel? Pairing
    {
        get => (DisplayPairingViewModel?)GetValue(PairingProperty);
        private set => SetValue(PairingProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(
            nameof(Theme),
            typeof(ThemeManager),
            typeof(DisplayPairingView),
            new PropertyMetadata(null));

    public ThemeManager? Theme
    {
        get => (ThemeManager?)GetValue(ThemeProperty);
        private set => SetValue(ThemeProperty, value);
    }

    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(
            nameof(Layout),
            typeof(ResponsiveLayoutManager),
            typeof(DisplayPairingView),
            new PropertyMetadata(null));

    public ResponsiveLayoutManager? Layout
    {
        get => (ResponsiveLayoutManager?)GetValue(LayoutProperty);
        private set => SetValue(LayoutProperty, value);
    }

    public DisplayPairingView()
    {
        InitializeComponent();
    }

    // Each composed part of the view model gets its own property so {x:Bind} paths stay one level deep.
    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DisplayPairingView view = (DisplayPairingView)d;
        if (view.Pairing is not null)
        {
            view.Pairing.PropertyChanged -= view.OnPairingPropertyChanged;
        }

        LyricsViewModel? viewModel = (LyricsViewModel?)e.NewValue;
        view.Pairing = viewModel?.Pairing;
        view.Theme = viewModel?.Theme;
        view.Layout = viewModel?.Layout;

        if (view.Pairing is not null)
        {
            view.Pairing.PropertyChanged += view.OnPairingPropertyChanged;
        }
        view.RenderQrCode();
    }

    private void OnPairingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplayPairingViewModel.QrCode))
        {
            RenderQrCode();
        }
    }

    private void RenderQrCode()
    {
        QrCanvas.Children.Clear();
        QrCodeMatrix? matrix = Pairing?.QrCode;
        if (matrix is null)
        {
            QrCanvas.Width = 0;
            QrCanvas.Height = 0;
            return;
        }

        double side = matrix.Size * QR_MODULE_SIZE;
        QrCanvas.Width = side;
        QrCanvas.Height = side;

        SolidColorBrush dark = new(Colors.Black);
        foreach (QrModuleRun run in matrix.GetDarkRuns())
        {
            Rectangle rectangle = new()
            {
                Width = run.Length * QR_MODULE_SIZE,
                Height = QR_MODULE_SIZE + QR_ROW_OVERLAP,
                Fill = dark,
            };
            Canvas.SetLeft(rectangle, run.Column * QR_MODULE_SIZE);
            Canvas.SetTop(rectangle, run.Row * QR_MODULE_SIZE);
            QrCanvas.Children.Add(rectangle);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Pairing?.CancelPairing();
    }

    public Thickness GetPagePadding(LayoutBreakpoint? breakpoint = null) =>
        (breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint) == LayoutBreakpoint.Small
            ? new Thickness(12)
            : new Thickness(32);

    public Thickness GetCardPadding(LayoutBreakpoint? breakpoint = null) =>
        (breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint) == LayoutBreakpoint.Small
            ? new Thickness(18)
            : new Thickness(40, 32, 40, 32);

    public double GetTitleFontSize(LayoutBreakpoint? breakpoint = null) =>
        (breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint) == LayoutBreakpoint.Small ? 22.0 : 30.0;

    public Orientation GetCodeOrientation(LayoutBreakpoint? breakpoint = null) =>
        (breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint) == LayoutBreakpoint.Small
            ? Orientation.Vertical
            : Orientation.Horizontal;

    public double GetQrMaxSize(LayoutBreakpoint? breakpoint = null) =>
        (breakpoint ?? ResponsiveLayoutManager.Instance.CurrentBreakpoint) switch
        {
            LayoutBreakpoint.Small => 240.0,
            LayoutBreakpoint.Medium => 300.0,
            _ => 380.0,
        };
}
