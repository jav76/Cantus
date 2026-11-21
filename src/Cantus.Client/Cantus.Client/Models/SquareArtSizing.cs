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
}
