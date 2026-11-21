using Cantus.Client.Icons;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cantus.Client.Views;

/// <summary>
/// Draws a <see cref="CantusIconKind"/> in a <see cref="Size"/> x <see cref="Size"/> box, filled with the inherited
/// Foreground. It is a PathIcon, so it also works where an IconElement is required (MenuFlyoutItem.Icon).
/// </summary>
public sealed partial class CantusIcon : PathIcon
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(CantusIconKind),
            typeof(CantusIcon),
            new PropertyMetadata(default(CantusIconKind), OnShapePropertyChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(CantusIcon),
            new PropertyMetadata(16.0, OnShapePropertyChanged));

    public CantusIcon()
    {
        UpdateShape();
    }

    public CantusIconKind Kind
    {
        get => (CantusIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Width and height of the icon box in pixels. Phosphor leaves 24 of 256 units of padding on each side.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnShapePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CantusIcon)d).UpdateShape();
    }

    private void UpdateShape()
    {
        double size = Size;
        Width = size;
        Height = size;
        Data = ToGeometry(CantusIconGeometry.GetPathMarkup(Kind, size));
    }

    private static Geometry ToGeometry(string markup)
    {
#if HAS_UNO
        return markup;
#else
        return (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), markup);
#endif
    }
}
