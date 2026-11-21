using System.Collections.Generic;
using Cantus.Client.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Cantus.Client.Views;

/// <summary>
/// Horizontal stack that shows as many leading children as fit and hides the rest.
/// Children are listed highest priority first. Hidden children are arranged at zero
/// size outside the panel's clip, so their Visibility is never touched during layout.
/// </summary>
public sealed partial class PriorityStackPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(PriorityStackPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private readonly List<double> _childWidths = new();
    private readonly RectangleGeometry _clip = new();
    private int _fittingCount;

    public PriorityStackPanel()
    {
        Clip = _clip;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PriorityStackPanel)d).InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size childConstraint = new(double.PositiveInfinity, availableSize.Height);
        _childWidths.Clear();

        foreach (UIElement child in Children)
        {
            child.Measure(childConstraint);
            _childWidths.Add(child.DesiredSize.Width);
        }

        _fittingCount = PriorityFitLayout.CountFitting(_childWidths, Spacing, availableSize.Width);

        double height = 0;
        for (int i = 0; i < _fittingCount; i++)
        {
            double childHeight = Children[i].DesiredSize.Height;
            if (childHeight > height)
            {
                height = childHeight;
            }
        }

        return new Size(PriorityFitLayout.GetUsedWidth(_childWidths, Spacing, _fittingCount), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        bool hasArrangedChild = false;

        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];
            double width = child.DesiredSize.Width;

            if (i < _fittingCount && width > 0)
            {
                if (hasArrangedChild)
                {
                    x += Spacing;
                }

                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width;
                hasArrangedChild = true;
            }
            else
            {
                child.Arrange(new Rect(-width - 1, 0, 0, 0));
            }
        }

        _clip.Rect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        return finalSize;
    }
}
