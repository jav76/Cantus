using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public class SpotifyPlayerClientTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetCurrentPlaybackAsync_WithInvalidToken_ReturnsNull(string? token)
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        Core.Models.PlaybackState? result = await client.GetCurrentPlaybackAsync(token!);
        result.Should().BeNull();
    }
}
