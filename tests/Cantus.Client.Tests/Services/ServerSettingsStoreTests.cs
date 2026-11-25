using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ServerSettingsStoreTests
{
    [Fact]
    public void NewDevice_HasNoServerAndSetupIsNotDone()
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());

        settings.SavedServerUrl.Should().BeNull();
        settings.IsSetupDone.Should().BeFalse();
    }

    [Fact]
    public void Save_StoresTheBaseUrlAndCompletesSetup()
    {
        InMemorySettingsStore store = new();
        ServerSettingsStore settings = new(store);

        bool saved = settings.Save(" http://192.168.0.10:5000/hubs/playback ");

        saved.Should().BeTrue();
        store.Values[ServerSettingsStore.SERVER_URL_KEY].Should().Be("http://192.168.0.10:5000");
        new ServerSettingsStore(store).SavedServerUrl.Should().Be("http://192.168.0.10:5000");
        settings.IsSetupDone.Should().BeTrue();
    }

    [Fact]
    public void Save_InvalidAddress_SavesNothing()
    {
        InMemorySettingsStore store = new();
        ServerSettingsStore settings = new(store);

        bool saved = settings.Save("192.168.0.10:5000");

        saved.Should().BeFalse();
        store.Values.Should().BeEmpty();
    }

    [Fact]
    public void SkipSetup_CompletesSetupWithoutAServer()
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());

        settings.SkipSetup();

        settings.SavedServerUrl.Should().BeNull();
        settings.IsSetupDone.Should().BeTrue();
    }

    [Fact]
    public void SkipSetup_AfterASave_ForgetsTheSavedServer()
    {
        ServerSettingsStore settings = new(new InMemorySettingsStore());
        settings.Save("http://192.168.0.10:5000");

        settings.SkipSetup();

        settings.SavedServerUrl.Should().BeNull();
        settings.IsSetupDone.Should().BeTrue();
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    public void IsSetupDone_ReadsAFlagStoredAsText(string stored, bool expected)
    {
        InMemorySettingsStore store = new();
        store.SetValue(ServerSettingsStore.SETUP_DONE_KEY, stored);

        new ServerSettingsStore(store).IsSetupDone.Should().Be(expected);
    }

    [Fact]
    public void SavedServerUrl_UnusableStoredValue_ReadsAsNothingSaved()
    {
        InMemorySettingsStore store = new();
        store.SetValue(ServerSettingsStore.SERVER_URL_KEY, "not a url");

        ServerSettingsStore settings = new(store);

        settings.SavedServerUrl.Should().BeNull();
        settings.IsSetupDone.Should().BeFalse();
    }
}
