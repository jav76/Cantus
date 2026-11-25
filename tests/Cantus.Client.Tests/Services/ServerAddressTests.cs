using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ServerAddressTests
{
    [Theory]
    [InlineData("http://192.168.0.10:5000", "http://192.168.0.10:5000")]
    [InlineData("http://192.168.0.10:5000/", "http://192.168.0.10:5000")]
    [InlineData("  https://lyrics.example.com  ", "https://lyrics.example.com")]
    [InlineData("HTTPS://Lyrics.Example.com", "https://lyrics.example.com")]
    [InlineData("http://192.168.0.10:5000?x=1#top", "http://192.168.0.10:5000")]
    [InlineData("http://192.168.0.10:5000/hubs/playback", "http://192.168.0.10:5000")]
    [InlineData("http://192.168.0.10:5000/hubs/playback/", "http://192.168.0.10:5000")]
    [InlineData("https://example.com/cantus/", "https://example.com/cantus")]
    public void TryNormalize_HttpAddress_ReturnsTheBaseUrl(string input, string expected)
    {
        bool accepted = ServerAddress.TryNormalize(input, out string baseUrl);

        accepted.Should().BeTrue();
        baseUrl.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("192.168.0.10:5000")]
    [InlineData("lyrics.example.com")]
    [InlineData("not a url")]
    [InlineData("ftp://192.168.0.10")]
    [InlineData("file:///etc/hosts")]
    [InlineData("/hubs/playback")]
    public void TryNormalize_AnythingElse_IsRejected(string? input)
    {
        bool accepted = ServerAddress.TryNormalize(input, out string baseUrl);

        accepted.Should().BeFalse();
        baseUrl.Should().BeEmpty();
    }

    [Fact]
    public void ToHubUrl_AppendsTheHubPath()
    {
        ServerAddress.ToHubUrl("https://example.com/cantus").Should().Be("https://example.com/cantus/hubs/playback");
    }
}
