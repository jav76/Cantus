using Cantus.Client.LocalPlayback;
using Cantus.Client.Tests.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class LocalPlayerPreferencesTests
{
    private readonly InMemorySettingsStore _store = new();

    [Theory]
    [InlineData("playerctld", false)]
    [InlineData("kdeconnect", false)]
    [InlineData("org.kde.kdeconnect.app", false)]
    [InlineData("kdeconnect.mpris_000001", false)]
    [InlineData("spotify", true)]
    [InlineData("brave", true)]
    public void IsEnabled_ByDefault_TurnsOffMirrorsAndPhones(string appId, bool expected)
    {
        new LocalPlayerPreferences(_store).IsEnabled(appId).Should().Be(expected);
    }

    [Fact]
    public void SetEnabled_False_StoresThePlayerAlongsideTheDefaults()
    {
        // Arrange
        LocalPlayerPreferences preferences = new(_store);

        // Act
        preferences.SetEnabled("brave", false);

        // Assert
        preferences.IsEnabled("brave").Should().BeFalse();
        preferences.IsEnabled("playerctld").Should().BeFalse();
        new LocalPlayerPreferences(_store).IsEnabled("brave").Should().BeFalse("the switch is persisted");
        _store.Values.Should().ContainKey(LocalPlayerPreferences.KEY_DISABLED_PLAYERS);
    }

    [Fact]
    public void SetEnabled_True_CanTurnADefaultBackOn()
    {
        // Arrange
        LocalPlayerPreferences preferences = new(_store);

        // Act
        preferences.SetEnabled("org.kde.kdeconnect.app", true);

        // Assert
        preferences.IsEnabled("org.kde.kdeconnect.app").Should().BeTrue();
        preferences.IsEnabled("playerctld").Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_IsCaseInsensitiveAndDoesNotDuplicate()
    {
        // Arrange
        LocalPlayerPreferences preferences = new(_store);

        // Act
        preferences.SetEnabled("Brave", false);
        preferences.SetEnabled("brave", false);

        // Assert
        preferences.DisabledPlayers.Should().ContainSingle(p => p == "Brave");
        preferences.SetEnabled("BRAVE", true);
        preferences.IsEnabled("brave").Should().BeTrue();
    }
}
