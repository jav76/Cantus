using System;

namespace Cantus.Client.Models;

/// <summary>
/// Pixel maths for the blurred album art backdrop: an area-average downscale
/// followed by repeated box blurs, which together approximate a wide Gaussian
/// at a fraction of the cost. Pure managed code, so the WASM DOM renderer and
/// desktop Skia produce the same image.
/// </summary>
public static class ArtworkBlur
{
    /// <summary>
    /// Size that fits within <paramref name="maxLongSide"/> on the longer axis,
    /// keeping the aspect ratio. Images already small enough keep their size.
    /// </summary>
    public static (int Width, int Height) GetScaledSize(int width, int height, int maxLongSide)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLongSide);

        int longSide = Math.Max(width, height);
        if (longSide <= maxLongSide)
        {
            return (width, height);
        }

        double scale = (double)maxLongSide / longSide;
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Area-average downscale, so every source pixel contributes and no detail aliases.</summary>
    public static RgbaImage Downscale(RgbaImage source, int maxLongSide)
    {
        ArgumentNullException.ThrowIfNull(source);
        (int width, int height) = GetScaledSize(source.Width, source.Height, maxLongSide);
        if (width == source.Width && height == source.Height)
        {
            return new RgbaImage(width, height, (byte[])source.Pixels.Clone());
        }

        byte[] src = source.Pixels;
        byte[] dst = new byte[width * height * RgbaImage.BYTES_PER_PIXEL];
        int srcStride = source.Width * RgbaImage.BYTES_PER_PIXEL;

        for (int y = 0; y < height; y++)
        {
            int y0 = y * source.Height / height;
            int y1 = Math.Max(y0 + 1, (y + 1) * source.Height / height);
            for (int x = 0; x < width; x++)
            {
                int x0 = x * source.Width / width;
                int x1 = Math.Max(x0 + 1, (x + 1) * source.Width / width);
                long r = 0, g = 0, b = 0, a = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    int rowStart = sy * srcStride;
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int i = rowStart + sx * RgbaImage.BYTES_PER_PIXEL;
                        r += src[i];
                        g += src[i + 1];
                        b += src[i + 2];
                        a += src[i + 3];
                    }
                }

                long count = (long)(x1 - x0) * (y1 - y0);
                int o = (y * width + x) * RgbaImage.BYTES_PER_PIXEL;
                dst[o] = (byte)((r + count / 2) / count);
                dst[o + 1] = (byte)((g + count / 2) / count);
                dst[o + 2] = (byte)((b + count / 2) / count);
                dst[o + 3] = (byte)((a + count / 2) / count);
            }
        }

        return new RgbaImage(width, height, dst);
    }

    /// <summary>
    /// Separable box blur repeated <paramref name="passes"/> times; three passes
    /// are close to a Gaussian. Edges clamp, so the borders do not darken.
    /// </summary>
    public static RgbaImage BoxBlur(RgbaImage source, int radius, int passes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfNegative(passes);

        byte[] current = (byte[])source.Pixels.Clone();
        if (radius == 0 || passes == 0)
        {
            return new RgbaImage(source.Width, source.Height, current);
        }

        byte[] scratch = new byte[current.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            BlurAxis(current, scratch, source.Width, source.Height, radius, horizontal: true);
            BlurAxis(scratch, current, source.Width, source.Height, radius, horizontal: false);
        }

        return new RgbaImage(source.Width, source.Height, current);
    }

    private static void BlurAxis(byte[] src, byte[] dst, int width, int height, int radius, bool horizontal)
    {
        int lineCount = horizontal ? height : width;
        int lineLength = horizontal ? width : height;
        int step = (horizontal ? 1 : width) * RgbaImage.BYTES_PER_PIXEL;
        int windowSize = radius * 2 + 1;
        int[] sums = new int[RgbaImage.BYTES_PER_PIXEL];

        for (int line = 0; line < lineCount; line++)
        {
            int lineStart = (horizontal ? line * width : line) * RgbaImage.BYTES_PER_PIXEL;
            Array.Clear(sums);
            for (int k = -radius; k <= radius; k++)
            {
                int i = lineStart + Math.Clamp(k, 0, lineLength - 1) * step;
                for (int c = 0; c < RgbaImage.BYTES_PER_PIXEL; c++)
                {
                    sums[c] += src[i + c];
                }
            }

            for (int p = 0; p < lineLength; p++)
            {
                int o = lineStart + p * step;
                for (int c = 0; c < RgbaImage.BYTES_PER_PIXEL; c++)
                {
                    dst[o + c] = (byte)((sums[c] + windowSize / 2) / windowSize);
                }

                int leaving = lineStart + Math.Clamp(p - radius, 0, lineLength - 1) * step;
                int entering = lineStart + Math.Clamp(p + radius + 1, 0, lineLength - 1) * step;
                for (int c = 0; c < RgbaImage.BYTES_PER_PIXEL; c++)
                {
                    sums[c] += src[entering + c] - src[leaving + c];
                }
            }
        }
    }
}
