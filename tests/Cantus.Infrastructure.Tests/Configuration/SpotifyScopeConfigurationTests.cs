using Cantus.Infrastructure;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Infrastructure.Tests.Configuration;

public sealed class SpotifyScopeConfigurationTests
{
    private static SpotifyOptions BindScopes(params string[] configuredScopes)
    {
        Dictionary<string, string?> values = new();
        for (int i = 0; i < configuredScopes.Length; i++)
        {
            values[$"Spotify:Scopes:{i}"] = configuredScopes[i];
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCantusInfrastructure(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SpotifyOptions>>().Value;
    }

    [Fact]
    public void CodeDefaults_ContainEveryScopeTheAppRequires()
    {
        // Configuration binding appends to collection defaults rather than
        // replacing them, so these defaults are what ultimately guarantees a
        // scope is requested - an appsettings list cannot remove one.
        SpotifyOptions defaults = new();

        defaults.Scopes.Should().Contain("user-modify-playback-state",
            "the transport controls receive 403 from Spotify without it");
        defaults.Scopes.Should().Contain("user-read-playback-state");
        defaults.Scopes.Should().Contain("user-read-currently-playing");
    }

    [Fact]
    public void ConfiguredScopes_OverlappingTheDefaults_AreNotSentTwice()
    {
        // Binding appends, so a scope named in both the defaults and
        // configuration used to appear twice in the authorize URL.
        SpotifyOptions options = BindScopes(
            "user-read-playback-state",
            "user-read-currently-playing",
            "user-read-private",
            "user-read-email");

        options.Scopes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ConfiguredScopes_AddNewEntriesWithoutDroppingDefaults()
    {
        SpotifyOptions options = BindScopes("playlist-read-private");

        options.Scopes.Should().Contain("playlist-read-private");
        options.Scopes.Should().Contain("user-modify-playback-state");
        options.Scopes.Should().OnlyHaveUniqueItems();
    }
}
