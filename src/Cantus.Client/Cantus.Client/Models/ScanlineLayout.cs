using System;

namespace Cantus.Client.Models;

/// <summary>Line maths for the scanline overlay: one thin line at the top of every period.</summary>
public static class ScanlineLayout
{
    /// <summary>Lines needed so the last period still covers <paramref name="extent"/>.</summary>
    public static int GetLineCount(double extent, double period)
    {
        if (period <= 0 || double.IsNaN(period) || double.IsInfinity(period))
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be a positive finite number.");
        }

        if (double.IsNaN(extent) || double.IsInfinity(extent) || extent <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(extent / period);
    }
}
