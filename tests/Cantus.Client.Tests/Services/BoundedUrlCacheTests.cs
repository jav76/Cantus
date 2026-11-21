using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class BoundedUrlCacheTests
{
    [Fact]
    public void Add_PastCapacity_EvictsTheOldestEntry()
    {
        // Arrange
        BoundedUrlCache<string> cache = new(2);

        // Act
        cache.Add("a", "first");
        cache.Add("b", "second");
        cache.Add("c", "third");

        // Assert
        cache.Count.Should().Be(2);
        cache.TryGet("a", out _).Should().BeFalse();
        cache.TryGet("c", out string? value).Should().BeTrue();
        value.Should().Be("third");
    }

    [Fact]
    public void Add_ExistingUrl_KeepsTheFirstValue()
    {
        // Arrange
        BoundedUrlCache<string> cache = new(2);

        // Act
        cache.Add("a", "first");
        cache.Add("a", "replacement");

        // Assert
        cache.TryGet("a", out string? value).Should().BeTrue();
        value.Should().Be("first");
        cache.Count.Should().Be(1);
    }
}
