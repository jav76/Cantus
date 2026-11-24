using System;

namespace Cantus.Client.Models;

/// <summary>
/// Height for album art that stretches to its width and should stay square
/// inside an auto-scrolling container. Where the vertical scrollbar takes
/// layout space, growing the art can show the scrollbar, which narrows the
/// art, which hides the scrollbar again. To break that loop the art only grows
/// when the scrollbar is already shown or the taller art still fits, so it can
/// end up a scrollbar's width shorter than it is wide.
/// </summary>
public static class SquareArtSizing
{
    private const double RESIZE_TOLERANCE = 0.5;

    /// <summary>The height to apply, or null to keep <paramref name="currentHeight"/> (NaN when never set).</summary>
    public static double? GetNextHeight(
        double currentHeight,
        double width,
        bool isVerticalScrollBarShown,
        double extentHeight,
        double viewportHeight)
    {
        if (width <= 0 || double.IsNaN(width))
        {
            return null;
        }

        if (double.IsNaN(currentHeight))
        {
            return width;
        }

        double growth = width - currentHeight;
        if (Math.Abs(growth) <= RESIZE_TOLERANCE)
        {
            return null;
        }

        if (growth > 0 && !isVerticalScrollBarShown && extentHeight + growth > viewportHeight + RESIZE_TOLERANCE)
        {
            return null;
        }

        return width;
    }

    /// <summary>
    /// The largest art side that keeps the whole panel inside <paramref name="viewportHeight"/>,
    /// never below <paramref name="minSize"/> (the panel scrolls instead).
    /// Everything but the art keeps its height when the art resizes, so applying the cap settles in one pass.
    /// </summary>
    /// <param name="contentHeight">The panel's full height, art included.</param>
    /// <param name="artHeight">The art's current height, or NaN before it is measured.</param>
    public static double GetHeightCap(double viewportHeight, double contentHeight, double artHeight, double minSize)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
        {
            return double.PositiveInfinity;
        }

        double rest = contentHeight - (double.IsNaN(artHeight) ? 0 : artHeight);
        return Math.Max(minSize, viewportHeight - rest);
    }
}
