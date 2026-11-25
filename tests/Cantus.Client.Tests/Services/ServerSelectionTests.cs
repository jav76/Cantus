using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ServerSelectionTests
{
    private const string SAVED_URL = "http://saved.example:5000";
    private const string OVERRIDE_URL = "http://override.example:5000";

    [Fact]
    public void Resolve_NothingConfigured_UsesThisComputerAndNeedsSetup()
    {
        ServerSelection selection = ServerSelection.Resolve(null, ServerUrlSource.Default, Settings());

        selection.BaseUrl.Should().Be(ServerAddress.DEFAULT_BASE_URL);
        selection.Source.Should().Be(ServerUrlSource.Default);
        selection.NeedsSetup.Should().BeTrue();
        selection.IsOverridden.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SkippedSetup_UsesThisComputerWithoutAsking()
    {
        ServerSettingsStore settings = Settings();
        settings.SkipSetup();

        ServerSelection selection = ServerSelection.Resolve(null, ServerUrlSource.Default, settings);

        selection.BaseUrl.Should().Be(ServerAddress.DEFAULT_BASE_URL);
        selection.NeedsSetup.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SavedServer_IsUsed()
    {
        ServerSelection selection = ServerSelection.Resolve(null, ServerUrlSource.Default, Settings(SAVED_URL));

        selection.BaseUrl.Should().Be(SAVED_URL);
        selection.HubUrl.Should().Be($"{SAVED_URL}/hubs/playback");
        selection.Source.Should().Be(ServerUrlSource.Saved);
        selection.NeedsSetup.Should().BeFalse();
        selection.IsOverridden.Should().BeFalse();
    }

    [Theory]
    [InlineData(ServerUrlSource.CommandLine)]
    [InlineData(ServerUrlSource.EnvironmentVariable)]
    public void Resolve_Override_WinsOverTheSavedServer(ServerUrlSource source)
    {
        ServerSelection selection = ServerSelection.Resolve($"{OVERRIDE_URL}/hubs/playback", source, Settings(SAVED_URL));

        selection.BaseUrl.Should().Be(OVERRIDE_URL);
        selection.Source.Should().Be(source);
        selection.IsOverridden.Should().BeTrue();
    }

    [Theory]
    [InlineData(ServerUrlSource.CommandLine)]
    [InlineData(ServerUrlSource.EnvironmentVariable)]
    public void Resolve_OverrideOnAFirstLaunch_DoesNotAsk(ServerUrlSource source)
    {
        ServerSelection selection = ServerSelection.Resolve(OVERRIDE_URL, source, Settings());

        selection.NeedsSetup.Should().BeFalse();
    }

    [Fact]
    public void Resolve_UnusableOverride_FallsBackToSettings()
    {
        ServerSelection selection = ServerSelection.Resolve("not a url", ServerUrlSource.CommandLine, Settings(SAVED_URL));

        selection.BaseUrl.Should().Be(SAVED_URL);
        selection.Source.Should().Be(ServerUrlSource.Saved);
    }

    private static ServerSettingsStore Settings(string? savedUrl = null)
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());
        if (savedUrl is not null)
        {
            settings.Save(savedUrl);
        }

        return settings;
    }
}
