using System;

namespace Cantus.Client.Models;

/// <summary>
/// Tile maths for the film grain overlay. The grain is a grid of identical
/// tiles one tile larger than the viewport in each direction, shifted by a
/// random amount every frame so the specks never sit still.
/// </summary>
public static class FilmGrainLayout
{
    /// <summary>
    /// Tiles needed along one axis so that the grid still covers
    /// <paramref name="extent"/> after it is shifted by up to one tile.
    /// </summary>
    public static int GetTileCount(double extent, double tileSize)
    {
        if (tileSize <= 0 || double.IsNaN(tileSize) || double.IsInfinity(tileSize))
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), tileSize, "Tile size must be a positive finite number.");
        }

        if (double.IsNaN(extent) || double.IsInfinity(extent) || extent <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(extent / tileSize) + 1;
    }

    /// <summary>
    /// Maps a uniform sample in [0, 1) to a whole-pixel shift in (-tileSize, 0].
    /// Shifting only towards the origin keeps the extra tile on the far edge in
    /// view, and whole pixels keep the specks sharp.
    /// </summary>
    public static double GetJitterOffset(double unitSample, double tileSize)
    {
        double clamped = Math.Clamp(unitSample, 0, 1);
        double offset = -Math.Floor(clamped * tileSize);
        return offset <= -tileSize ? 0 : offset;
    }

    /// <summary>Next grain layer to show, wrapping after the last one.</summary>
    public static int GetNextLayerIndex(int currentIndex, int layerCount)
    {
        if (layerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layerCount), layerCount, "There must be at least one layer.");
        }

        return currentIndex < 0 ? 0 : (currentIndex + 1) % layerCount;
    }
}
