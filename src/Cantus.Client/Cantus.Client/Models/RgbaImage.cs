using System;

namespace Cantus.Client.Models;

/// <summary>An 8-bit RGBA bitmap: four bytes per pixel, rows top to bottom.</summary>
public sealed class RgbaImage
{
    public const int BYTES_PER_PIXEL = 4;

    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != (long)width * height * BYTES_PER_PIXEL)
        {
            throw new ArgumentException("Pixel buffer length must be width * height * 4.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }
}
