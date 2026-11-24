using System;
using StbImageSharp;

namespace Cantus.Client.Models;

/// <summary>Converts an image file into the value of the X11 <c>_NET_WM_ICON</c> window property.</summary>
public static class WindowIconProperty
{
    private const int HEADER_LENGTH = 2;
    private const int BYTES_PER_PIXEL = 4;

    /// <summary>
    /// Width, height, then one ARGB value per pixel, row by row. Xlib takes 32-bit property data as
    /// native longs, hence <see cref="nint"/>.
    /// </summary>
    public static nint[] Build(byte[] imageFile)
    {
        ImageResult image = ImageResult.FromMemory(imageFile, ColorComponents.RedGreenBlueAlpha);
        int pixelCount = image.Width * image.Height;
        nint[] property = new nint[HEADER_LENGTH + pixelCount];
        property[0] = image.Width;
        property[1] = image.Height;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int offset = pixel * BYTES_PER_PIXEL;
            uint argb = ((uint)image.Data[offset + 3] << 24)
                | ((uint)image.Data[offset] << 16)
                | ((uint)image.Data[offset + 1] << 8)
                | image.Data[offset + 2];
            property[HEADER_LENGTH + pixel] = (nint)argb;
        }

        return property;
    }
}
