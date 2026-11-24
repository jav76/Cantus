using System;
using Cantus.Client.LocalPlayback;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class PublishWorthinessTests
{
    [Theory]
    [InlineData(30_000, true)]
    [InlineData(184_000, true)]
    [InlineData(1_200_000, true)]
    [InlineData(29_999, false)]
    [InlineData(1_200_001, false)]
    [InlineData(0, false)]
    public void IsWorthPublishing_AcceptsSongsFrom30SecondsTo20Minutes(long durationMs, bool expected)
    {
        // Arrange
        LocalTrack track = new("Ballin", "parkbreezy", null, TimeSpan.FromMilliseconds(durationMs), null, null);

        // Act & Assert
        PublishWorthiness.IsWorthPublishing(track).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "parkbreezy")]
    [InlineData("   ", "parkbreezy")]
    [InlineData("Ballin", "")]
    [InlineData("Ballin", " \t")]
    public void IsWorthPublishing_NeedsATitleAndAnArtist(string title, string artist)
    {
        // Arrange
        LocalTrack track = new(title, artist, null, TimeSpan.FromMinutes(3), null, null);

        // Act & Assert
        PublishWorthiness.IsWorthPublishing(track).Should().BeFalse();
    }

    [Fact]
    public void IsWorthPublishing_NoTrack_IsFalse()
    {
        PublishWorthiness.IsWorthPublishing(null).Should().BeFalse();
    }
}
