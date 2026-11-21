using System;
using System.Linq;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class ArtworkBlurTests
{
    private const int MAX_LONG_SIDE = 80;
    private const int BLUR_RADIUS = 4;
    private const int BLUR_PASSES = 3;
    private const int CHECKER_SIZE = 64;
    private const double AVERAGE_TOLERANCE = 2.0;

    [Theory]
    [InlineData(640, 640, 80, 80)]
    [InlineData(640, 320, 80, 40)]
    [InlineData(300, 600, 40, 80)]
    [InlineData(64, 48, 64, 48)]
    [InlineData(1000, 3, 80, 1)]
    public void GetScaledSize_FitsTheLongSideAndKeepsAspect(int width, int height, int expectedWidth, int expectedHeight)
    {
        // Act
        (int scaledWidth, int scaledHeight) = ArtworkBlur.GetScaledSize(width, height, MAX_LONG_SIDE);

        // Assert
        scaledWidth.Should().Be(expectedWidth);
        scaledHeight.Should().Be(expectedHeight);
    }

    [Fact]
    public void Downscale_AveragesEverySourcePixel()
    {
        // Arrange: a 2x2 image of black, white, black, white averages to mid grey.
        RgbaImage source = new(2, 2,
        [
            0, 0, 0, 255, 255, 255, 255, 255,
            0, 0, 0, 255, 255, 255, 255, 255,
        ]);

        // Act
        RgbaImage scaled = ArtworkBlur.Downscale(source, 1);

        // Assert
        scaled.Width.Should().Be(1);
        scaled.Height.Should().Be(1);
        scaled.Pixels.Should().Equal(128, 128, 128, 255);
    }

    [Fact]
    public void Downscale_LargeImage_ProducesTheScaledSize()
    {
        // Arrange
        RgbaImage source = CreateChecker(640, 480);

        // Act
        RgbaImage scaled = ArtworkBlur.Downscale(source, MAX_LONG_SIDE);

        // Assert
        scaled.Width.Should().Be(80);
        scaled.Height.Should().Be(60);
        scaled.Pixels.Length.Should().Be(80 * 60 * RgbaImage.BYTES_PER_PIXEL);
    }

    [Fact]
    public void BoxBlur_KeepsSizeAndRoughlyPreservesTheAverageColor()
    {
        // Arrange
        RgbaImage source = CreateChecker(CHECKER_SIZE, CHECKER_SIZE);

        // Act
        RgbaImage blurred = ArtworkBlur.BoxBlur(source, BLUR_RADIUS, BLUR_PASSES);

        // Assert
        blurred.Width.Should().Be(source.Width);
        blurred.Height.Should().Be(source.Height);
        for (int channel = 0; channel < RgbaImage.BYTES_PER_PIXEL; channel++)
        {
            ChannelAverage(blurred, channel).Should().BeApproximately(ChannelAverage(source, channel), AVERAGE_TOLERANCE);
        }
    }

    [Fact]
    public void BoxBlur_SmoothsHardEdges()
    {
        // Arrange
        RgbaImage source = CreateChecker(CHECKER_SIZE, CHECKER_SIZE);

        // Act
        RgbaImage blurred = ArtworkBlur.BoxBlur(source, BLUR_RADIUS, BLUR_PASSES);

        // Assert
        ChannelRange(blurred, 0).Should().BeLessThan(ChannelRange(source, 0) / 2);
    }

    [Fact]
    public void BoxBlur_SolidColor_IsUnchanged()
    {
        // Arrange
        byte[] pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 200, 40, 90, 255 }).ToArray();
        RgbaImage source = new(16, 16, pixels);

        // Act
        RgbaImage blurred = ArtworkBlur.BoxBlur(source, BLUR_RADIUS, BLUR_PASSES);

        // Assert
        blurred.Pixels.Should().Equal(pixels);
    }

    [Fact]
    public void BoxBlur_DoesNotModifyTheSource()
    {
        // Arrange
        RgbaImage source = CreateChecker(16, 16);
        byte[] original = (byte[])source.Pixels.Clone();

        // Act
        ArtworkBlur.BoxBlur(source, BLUR_RADIUS, BLUR_PASSES);

        // Assert
        source.Pixels.Should().Equal(original);
    }

    [Fact]
    public void RgbaImage_RejectsAMismatchedBuffer()
    {
        // Act
        Action create = () => _ = new RgbaImage(2, 2, new byte[3]);

        // Assert
        create.Should().Throw<ArgumentException>();
    }

    private static RgbaImage CreateChecker(int width, int height)
    {
        byte[] pixels = new byte[width * height * RgbaImage.BYTES_PER_PIXEL];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool light = ((x / 4) + (y / 4)) % 2 == 0;
                int i = (y * width + x) * RgbaImage.BYTES_PER_PIXEL;
                pixels[i] = light ? (byte)230 : (byte)20;
                pixels[i + 1] = light ? (byte)120 : (byte)60;
                pixels[i + 2] = light ? (byte)10 : (byte)200;
                pixels[i + 3] = 255;
            }
        }

        return new RgbaImage(width, height, pixels);
    }

    private static double ChannelAverage(RgbaImage image, int channel) =>
        Enumerable.Range(0, image.Width * image.Height)
            .Average(p => image.Pixels[p * RgbaImage.BYTES_PER_PIXEL + channel]);

    private static int ChannelRange(RgbaImage image, int channel)
    {
        int[] values = Enumerable.Range(0, image.Width * image.Height)
            .Select(p => (int)image.Pixels[p * RgbaImage.BYTES_PER_PIXEL + channel])
            .ToArray();
        return values.Max() - values.Min();
    }
}
