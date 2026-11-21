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

    /// <summary>
    /// Area-average downscale, so every source pixel contributes and no detail
    /// aliases. Colour is weighted by alpha, so the arbitrary RGB stored under
    /// transparent pixels does not tint the result.
    /// </summary>
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
                        int alpha = src[i + 3];
                        r += src[i] * alpha;
                        g += src[i + 1] * alpha;
                        b += src[i + 2] * alpha;
                        a += alpha;
                    }
                }

                long count = (long)(x1 - x0) * (y1 - y0);
                int o = (y * width + x) * RgbaImage.BYTES_PER_PIXEL;
                if (a > 0)
                {
                    dst[o] = (byte)((r + a / 2) / a);
                    dst[o + 1] = (byte)((g + a / 2) / a);
                    dst[o + 2] = (byte)((b + a / 2) / a);
                }

                dst[o + 3] = (byte)((a + count / 2) / count);
            }
        }

        return new RgbaImage(width, height, dst);
    }

    /// <summary>
    /// Separable box blur repeated <paramref name="passes"/> times; three passes
    /// are close to a Gaussian. Edges clamp, so the borders do not darken. The
    /// blur runs on premultiplied alpha, so transparent pixels do not bleed
    /// their hidden colour into the opaque art.
    /// </summary>
    public static RgbaImage BoxBlur(RgbaImage source, int radius, int passes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfNegative(passes);

        if (radius == 0 || passes == 0)
        {
            return new RgbaImage(source.Width, source.Height, (byte[])source.Pixels.Clone());
        }

        int[] current = Premultiply(source.Pixels);
        int[] scratch = new int[current.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            BlurAxis(current, scratch, source.Width, source.Height, radius, horizontal: true);
            BlurAxis(scratch, current, source.Width, source.Height, radius, horizontal: false);
        }

        return new RgbaImage(source.Width, source.Height, Unpremultiply(current));
    }

    // Every channel, alpha included, is scaled to 0..255*255 so one blur
    // handles all four and the colour keeps full precision under low alpha.
    private static int[] Premultiply(byte[] pixels)
    {
        int[] result = new int[pixels.Length];
        for (int i = 0; i < pixels.Length; i += RgbaImage.BYTES_PER_PIXEL)
        {
            int alpha = pixels[i + 3];
            result[i] = pixels[i] * alpha;
            result[i + 1] = pixels[i + 1] * alpha;
            result[i + 2] = pixels[i + 2] * alpha;
            result[i + 3] = alpha * byte.MaxValue;
        }

        return result;
    }

    private static byte[] Unpremultiply(int[] premultiplied)
    {
        byte[] result = new byte[premultiplied.Length];
        for (int i = 0; i < premultiplied.Length; i += RgbaImage.BYTES_PER_PIXEL)
        {
            int alpha = premultiplied[i + 3];
            result[i + 3] = (byte)((alpha + byte.MaxValue / 2) / byte.MaxValue);
            if (alpha == 0)
            {
                continue;
            }

            for (int c = 0; c < 3; c++)
            {
                long scaled = ((long)premultiplied[i + c] * byte.MaxValue + alpha / 2) / alpha;
                result[i + c] = (byte)Math.Min(byte.MaxValue, scaled);
            }
        }

        return result;
    }

    private static void BlurAxis(int[] src, int[] dst, int width, int height, int radius, bool horizontal)
    {
        int lineCount = horizontal ? height : width;
        int lineLength = horizontal ? width : height;
        int step = (horizontal ? 1 : width) * RgbaImage.BYTES_PER_PIXEL;
        int windowSize = radius * 2 + 1;
        long[] sums = new long[RgbaImage.BYTES_PER_PIXEL];

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
                    dst[o + c] = (int)((sums[c] + windowSize / 2) / windowSize);
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
