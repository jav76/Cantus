using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class OwnSpotifyAppStoreTests
{
    private const string OWN_CLIENT_ID = "fedcba9876543210fedcba9876543210";

    [Fact]
    public void Save_StoresTheNormalizedClientId()
    {
        InMemorySettingsStore settings = new();
        OwnSpotifyAppStore store = new(settings);

        store.Save($" {OWN_CLIENT_ID.ToUpperInvariant()} ").Should().BeTrue();

        store.ClientId.Should().Be(OWN_CLIENT_ID);
        settings.Values[OwnSpotifyAppStore.CLIENT_ID_KEY].Should().Be(OWN_CLIENT_ID);
    }

    [Fact]
    public void Save_IgnoresAnInvalidClientId()
    {
        OwnSpotifyAppStore store = new(new InMemorySettingsStore());

        store.Save("not-a-client-id").Should().BeFalse();

        store.ClientId.Should().BeNull();
    }

    [Fact]
    public void ClientId_IgnoresAStoredValueThatIsNotAClientId()
    {
        InMemorySettingsStore settings = new();
        settings.SetValue(OwnSpotifyAppStore.CLIENT_ID_KEY, "garbage");

        new OwnSpotifyAppStore(settings).ClientId.Should().BeNull();
    }

    [Fact]
    public void Forget_ClearsTheSavedClientId()
    {
        OwnSpotifyAppStore store = new(new InMemorySettingsStore());
        store.Save(OWN_CLIENT_ID);

        store.Forget();

        store.ClientId.Should().BeNull();
    }
}
