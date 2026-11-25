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
    [InlineData("http://192.168.0.10:5000?x=1", "http://192.168.0.10:5000/hubs/playback")]
    [InlineData("http://192.168.0.10:5000#top", "http://192.168.0.10:5000/hubs/playback")]
    [InlineData("http://192.168.0.10:5000/?next=/hubs/playback", "http://192.168.0.10:5000/hubs/playback")]
    [InlineData("https://example.com/cantus/", "https://example.com/cantus/hubs/playback")]
    public void TrySetServerUrl_BaseAddress_AppendsHubPath(string input, string expected)
    {
        bool accepted = ClientStartupOptions.TrySetServerUrl(input);

        accepted.Should().BeTrue();
        ClientStartupOptions.ServerUrl.Should().Be(expected);
    }

    [Theory]
    [InlineData("http://192.168.0.10:5000/hubs/playback")]
    [InlineData("http://192.168.0.10:5000/hubs/playback/")]
    public void TrySetServerUrl_FullHubUrl_IsLeftAlone(string input)
    {
        bool accepted = ClientStartupOptions.TrySetServerUrl(input);

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

        client.ServerBaseUrl.Should().Be("http://127.0.0.1:5000");
    }

    [Fact]
    public void ApplyServerOverride_CommandLine_WinsOverTheEnvironment()
    {
        ServerOverrideResult result = ClientStartupOptions.ApplyServerOverride("http://cli.example:5000", "http://env.example:5000");

        result.Should().Be(ServerOverrideResult.Applied);
        ClientStartupOptions.ServerUrl.Should().Be("http://cli.example:5000/hubs/playback");
        ClientStartupOptions.ServerUrlSource.Should().Be(ServerUrlSource.CommandLine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ApplyServerOverride_NoCommandLineValue_UsesTheEnvironment(string? commandLineValue)
    {
        ServerOverrideResult result = ClientStartupOptions.ApplyServerOverride(commandLineValue, "http://env.example:5000");

        result.Should().Be(ServerOverrideResult.Applied);
        ClientStartupOptions.ServerUrl.Should().Be("http://env.example:5000/hubs/playback");
        ClientStartupOptions.ServerUrlSource.Should().Be(ServerUrlSource.EnvironmentVariable);
    }

    [Fact]
    public void ApplyServerOverride_Neither_LeavesTheDefault()
    {
        ClientStartupOptions.ApplyServerOverride(null, "").Should().Be(ServerOverrideResult.None);

        ClientStartupOptions.ServerUrl.Should().BeNull();
        ClientStartupOptions.ServerUrlSource.Should().Be(ServerUrlSource.Default);
    }

    [Fact]
    public void ApplyServerOverride_InvalidCommandLine_IsReportedAndDoesNotFallThroughToTheEnvironment()
    {
        ServerOverrideResult result = ClientStartupOptions.ApplyServerOverride("not a url", "http://env.example:5000");

        result.Should().Be(ServerOverrideResult.InvalidCommandLine);
        ClientStartupOptions.ServerUrl.Should().BeNull();
    }

    [Fact]
    public void ApplyServerOverride_InvalidEnvironment_IsReported()
    {
        ClientStartupOptions.ApplyServerOverride(null, "not a url").Should().Be(ServerOverrideResult.InvalidEnvironmentVariable);

        ClientStartupOptions.ServerUrl.Should().BeNull();
    }

    [Fact]
    public void ResolveServer_FirstLaunch_KeepsTheDefaultAndAsksForSetup()
    {
        ServerSelection selection = ClientStartupOptions.ResolveServer(new ServerSettingsStore(new InMemorySettingsStore()));

        selection.NeedsSetup.Should().BeTrue();
        ClientStartupOptions.ServerUrl.Should().BeNull();
        ClientStartupOptions.Selection.Should().Be(selection);
    }

    [Fact]
    public void ResolveServer_SavedServer_BecomesTheServerUrl()
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());
        settings.Save("http://saved.example:5000");

        ServerSelection selection = ClientStartupOptions.ResolveServer(settings);

        selection.Source.Should().Be(ServerUrlSource.Saved);
        ClientStartupOptions.ServerUrl.Should().Be("http://saved.example:5000/hubs/playback");
    }

    [Fact]
    public void ResolveServer_WithAnOverride_IgnoresTheSavedServerAndDoesNotAsk()
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());
        ClientStartupOptions.ApplyServerOverride(null, "http://env.example:5000");

        ServerSelection firstLaunch = ClientStartupOptions.ResolveServer(settings);
        settings.Save("http://saved.example:5000");
        ServerSelection withSaved = ClientStartupOptions.ResolveServer(settings);

        firstLaunch.NeedsSetup.Should().BeFalse();
        withSaved.BaseUrl.Should().Be("http://env.example:5000");
        withSaved.Source.Should().Be(ServerUrlSource.EnvironmentVariable);
        ClientStartupOptions.ServerUrl.Should().Be("http://env.example:5000/hubs/playback");
    }

    [Fact]
    public void ResolveServer_AfterTheFirstRunPromptSaves_PicksUpTheNewServer()
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());
        ClientStartupOptions.ResolveServer(settings);

        settings.Save("http://saved.example:5000");
        ServerSelection selection = ClientStartupOptions.ResolveServer(settings);

        selection.NeedsSetup.Should().BeFalse();
        ClientStartupOptions.ServerUrl.Should().Be("http://saved.example:5000/hubs/playback");
    }
}
