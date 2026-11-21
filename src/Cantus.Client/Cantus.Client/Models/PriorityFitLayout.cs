using System;
using System.Collections.Generic;

namespace Cantus.Client.Models;

/// <summary>
/// Fit maths for a horizontal row whose items are listed highest priority first.
/// Items are kept in order until one no longer fits; it and every item after it
/// are dropped, so a lower-priority item never shows while a higher one is hidden.
/// Zero-width items (collapsed children) always fit and take no spacing.
/// </summary>
public static class PriorityFitLayout
{
    /// <summary>Number of leading items that fit in <paramref name="availableWidth"/>.</summary>
    public static int CountFitting(IReadOnlyList<double> widths, double spacing, double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(widths);

        if (double.IsNaN(availableWidth) || double.IsPositiveInfinity(availableWidth))
        {
            return widths.Count;
        }

        double used = 0;
        bool hasVisibleItem = false;
        for (int i = 0; i < widths.Count; i++)
        {
            double width = Math.Max(0, widths[i]);
            if (width <= 0)
            {
                continue;
            }

            double next = used + (hasVisibleItem ? spacing : 0) + width;
            if (next > availableWidth + 0.5)
            {
                return i;
            }

            used = next;
            hasVisibleItem = true;
        }

        return widths.Count;
    }

    /// <summary>Width of the first <paramref name="count"/> items laid out with <paramref name="spacing"/>.</summary>
    public static double GetUsedWidth(IReadOnlyList<double> widths, double spacing, int count)
    {
        ArgumentNullException.ThrowIfNull(widths);

        double used = 0;
        bool hasVisibleItem = false;
        int limit = Math.Min(count, widths.Count);
        for (int i = 0; i < limit; i++)
        {
            double width = Math.Max(0, widths[i]);
            if (width <= 0)
            {
                continue;
            }

            used += (hasVisibleItem ? spacing : 0) + width;
            hasVisibleItem = true;
        }

        return used;
    }
}
