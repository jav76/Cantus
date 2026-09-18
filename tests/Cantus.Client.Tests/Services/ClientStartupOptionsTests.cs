using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ClientStartupOptionsTests : IDisposable
{
    public ClientStartupOptionsTests()
    {
        ClientStartupOptions.ResetForTesting();
    }

    public void Dispose()
    {
        ClientStartupOptions.ResetForTesting();
    }

    [Theory]
    [InlineData("http://192.168.0.10:5000", "http://192.168.0.10:5000/hubs/playback")]
    [InlineData("http://192.168.0.10:5000/", "http://192.168.0.10:5000/hubs/playback")]
    [InlineData("https://lyrics.example.com", "https://lyrics.example.com/hubs/playback")]
    [InlineData("  http://192.168.0.10:5000  ", "http://192.168.0.10:5000/hubs/playback")]
    public void TrySetServerUrl_BaseAddress_AppendsHubPath(string input, string expected)
    {
        bool accepted = ClientStartupOptions.TrySetServerUrl(input);

        accepted.Should().BeTrue();
        ClientStartupOptions.ServerUrl.Should().Be(expected);
    }

    [Fact]
    public void TrySetServerUrl_FullHubUrl_IsLeftAlone()
    {
        bool accepted = ClientStartupOptions.TrySetServerUrl("http://192.168.0.10:5000/hubs/playback");

        accepted.Should().BeTrue();
        ClientStartupOptions.ServerUrl.Should().Be("http://192.168.0.10:5000/hubs/playback");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("192.168.0.10:5000")]
    [InlineData("not a url")]
    [InlineData("ftp://192.168.0.10")]
    [InlineData("/hubs/playback")]
    public void TrySetServerUrl_InvalidValue_IsRejectedAndLeavesDefaultResolution(string? input)
    {
        bool accepted = ClientStartupOptions.TrySetServerUrl(input);

        accepted.Should().BeFalse();
        ClientStartupOptions.ServerUrl.Should().BeNull(
            "an unusable value must not stop the client from starting or connecting");
    }

    [Fact]
    public void ServerUrl_WhenSet_IsUsedAsTheClientsHubAddress()
    {
        ClientStartupOptions.TrySetServerUrl("http://192.168.0.10:5000");

        SignalRPlaybackClient client = new(ClientStartupOptions.ServerUrl);

        client.ServerBaseUrl.Should().Be("http://192.168.0.10:5000");
    }

    [Fact]
    public void ServerUrl_WhenUnset_ClientFallsBackToItsDefault()
    {
        SignalRPlaybackClient client = new(ClientStartupOptions.ServerUrl);

        client.ServerBaseUrl.Should().Be("http://localhost:5000");
    }
}
