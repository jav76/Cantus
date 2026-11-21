using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class DisplayCredentialStoreTests
{
    [Fact]
    public void Load_WithNothingStored_ReturnsNull()
    {
        // Arrange
        DisplayCredentialStore store = new(new InMemorySettingsStore());

        // Act & Assert
        store.Load().Should().BeNull();
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheCredential()
    {
        // Arrange
        InMemorySettingsStore settings = new();
        DisplayCredentialStore store = new(settings);

        // Act
        store.Save(new DisplayCredential("display-1", "Living room TV", "token-1"));

        // Assert
        new DisplayCredentialStore(settings).Load()
            .Should().Be(new DisplayCredential("display-1", "Living room TV", "token-1"));
    }

    [Fact]
    public void Clear_ForgetsTheCredential()
    {
        // Arrange
        InMemorySettingsStore settings = new();
        DisplayCredentialStore store = new(settings);
        store.Save(new DisplayCredential("display-1", "Living room TV", "token-1"));

        // Act
        store.Clear();

        // Assert
        store.Load().Should().BeNull();
        settings.Values[DisplayCredentialStore.TOKEN_KEY].Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_WithABlankToken_ReturnsNull(string token)
    {
        // Arrange
        InMemorySettingsStore settings = new();
        settings.SetValue(DisplayCredentialStore.TOKEN_KEY, token);

        // Act & Assert
        new DisplayCredentialStore(settings).Load().Should().BeNull();
    }

    [Fact]
    public void ToString_RedactsTheToken()
    {
        // Act
        string text = new DisplayCredential("display-1", "TV", "secret-token").ToString();

        // Assert
        text.Should().NotContain("secret-token").And.Contain("[REDACTED]");
    }
}
