using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Cantus.Client.Models;

/// <summary>
/// Minimal PNG writer for 8-bit RGBA images: IHDR, one zlib IDAT with no row
/// filtering, IEND. Enough for small generated bitmaps that a BitmapImage
/// loads from a stream on every renderer.
/// </summary>
public static class PngEncoder
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const byte BIT_DEPTH = 8;
    private const byte COLOR_TYPE_RGBA = 6;
    private const byte FILTER_NONE = 0;

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using MemoryStream output = new();
        output.Write(Signature);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = BIT_DEPTH;
        header[9] = COLOR_TYPE_RGBA;
        WriteChunk(output, "IHDR"u8, header);

        WriteChunk(output, "IDAT"u8, CompressRows(image));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    /// <summary>Standard PNG/zlib CRC-32 (polynomial 0xEDB88320).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data, uint crc = 0)
    {
        crc ^= 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] CompressRows(RgbaImage image)
    {
        int stride = image.Width * RgbaImage.BYTES_PER_PIXEL;
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int row = 0; row < image.Height; row++)
            {
                zlib.WriteByte(FILTER_NONE);
                zlib.Write(image.Pixels, row * stride, stride);
            }
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(data, Crc32(type)));
        output.Write(buffer);
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
