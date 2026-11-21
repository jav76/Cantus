using System;
using System.Buffers.Binary;
using System.Text;
using Cantus.Client.Models;
using FluentAssertions;
using StbImageSharp;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class PngEncoderTests
{
    private const int WIDTH = 5;
    private const int HEIGHT = 3;
    private const int SIGNATURE_LENGTH = 8;
    private const int IHDR_LENGTH = 13;

    [Fact]
    public void Crc32_MatchesTheStandardCheckValue()
    {
        // Act
        uint crc = PngEncoder.Crc32("123456789"u8);

        // Assert
        crc.Should().Be(0xCBF43926u);
    }

    [Fact]
    public void Encode_StartsWithTheSignatureAndAnRgbaHeader()
    {
        // Act
        byte[] png = PngEncoder.Encode(CreateGradient());

        // Assert
        png.AsSpan(0, SIGNATURE_LENGTH).ToArray().Should().Equal(PngEncoder.Signature.ToArray());
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8)).Should().Be(IHDR_LENGTH);
        Encoding.ASCII.GetString(png, 12, 4).Should().Be("IHDR");
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)).Should().Be(WIDTH);
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)).Should().Be(HEIGHT);
        png[24].Should().Be(8, "8 bits per channel");
        png[25].Should().Be(6, "color type 6 is RGBA");
        Encoding.ASCII.GetString(png, png.Length - 8, 4).Should().Be("IEND");
    }

    [Fact]
    public void Encode_EveryChunkCrcCoversTypeAndData()
    {
        // Arrange
        byte[] png = PngEncoder.Encode(CreateGradient());
        int offset = SIGNATURE_LENGTH;
        int chunkCount = 0;

        // Act & Assert
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            ReadOnlySpan<byte> typeAndData = png.AsSpan(offset + 4, 4 + length);
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            storedCrc.Should().Be(PngEncoder.Crc32(typeAndData));
            offset += 12 + length;
            chunkCount++;
        }

        offset.Should().Be(png.Length);
        chunkCount.Should().Be(3);
    }

    [Fact]
    public void Encode_RoundTripsThroughStbImageSharp()
    {
        // Arrange
        RgbaImage image = CreateGradient();

        // Act
        ImageResult decoded = ImageResult.FromMemory(PngEncoder.Encode(image), ColorComponents.RedGreenBlueAlpha);

        // Assert
        decoded.Width.Should().Be(WIDTH);
        decoded.Height.Should().Be(HEIGHT);
        decoded.Data.Should().Equal(image.Pixels);
    }

    private static RgbaImage CreateGradient()
    {
        byte[] pixels = new byte[WIDTH * HEIGHT * RgbaImage.BYTES_PER_PIXEL];
        for (int i = 0; i < WIDTH * HEIGHT; i++)
        {
            pixels[i * 4] = (byte)(i * 17);
            pixels[i * 4 + 1] = (byte)(255 - i * 9);
            pixels[i * 4 + 2] = (byte)(i * 31 % 256);
            pixels[i * 4 + 3] = (byte)(i % 2 == 0 ? 255 : 128);
        }

        return new RgbaImage(WIDTH, HEIGHT, pixels);
    }
}
