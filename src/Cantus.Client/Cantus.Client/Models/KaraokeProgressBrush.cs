using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cantus.Client.Models;

/// <summary>
/// The one brush every karaoke underline shares. It is mutated in place, so bound lines repaint
/// without property-changed churn. The brush is created on first use because XAML brushes cannot
/// be constructed in headless tests, while the colour can be set at any time.
/// </summary>
internal static class KaraokeProgressBrush
{
    public static readonly Color DefaultColor = Color.FromArgb(255, 192, 132, 252);

    private static Color _color = DefaultColor;
    private static SolidColorBrush? _brush;

    /// <summary>The underline follows the artwork-derived accent only under the Dynamic theme.</summary>
    public static Color GetColor(ThemeMode mode, ColorPalette palette) =>
        mode == ThemeMode.Dynamic ? palette.SecondaryAccent : DefaultColor;

    public static SolidColorBrush Brush => _brush ??= new SolidColorBrush(_color);

    public static void SetColor(Color color)
    {
        _color = color;
        if (_brush is not null)
        {
            _brush.Color = color;
        }
    }
}
