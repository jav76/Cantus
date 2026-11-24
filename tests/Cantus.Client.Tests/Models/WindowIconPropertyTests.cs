using System;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class WindowIconPropertyTests
{
    // A 2 x 1 PNG: opaque red, then half-transparent blue.
    private const string TWO_PIXEL_PNG_BASE64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8AAQg0AD3oDfnfpf5cAAAAASUVORK5CYII=";

    [Fact]
    public void Build_WritesWidthHeightThenArgbPixels()
    {
        nint[] property = WindowIconProperty.Build(Convert.FromBase64String(TWO_PIXEL_PNG_BASE64));

        nint opaqueRed = unchecked((nint)0xFFFF0000L);
        nint halfTransparentBlue = unchecked((nint)0x800000FFL);
        property.Should().Equal(2, 1, opaqueRed, halfTransparentBlue);
    }
}
