using Cantus.Infrastructure.Spotify;
using Cantus.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Server.Tests.Services;

public sealed class HostUrlResolverTests
{
    [Fact]
    public void ResolveBaseUrl_WhenCantusHostUrlConfigured_ReturnsConfiguredUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CANTUS_HOST_URL"] = "https://lyrics.example.com/"
            })
            .Build();

        var options = Options.Create(new SpotifyOptions());
        var resolver = new HostUrlResolver(config, options);

        var result = resolver.ResolveBaseUrl();

        result.Should().Be("https://lyrics.example.com");
    }

    [Fact]
    public void ResolveBaseUrl_WhenHostUrlAliasConfigured_ReturnsConfiguredUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HOST_URL"] = "http://192.168.1.100:5000"
            })
            .Build();

        var options = Options.Create(new SpotifyOptions());
        var resolver = new HostUrlResolver(config, options);

        var result = resolver.ResolveBaseUrl();

        result.Should().Be("http://192.168.1.100:5000");
    }

    [Fact]
    public void ResolveBaseUrl_WhenNoEnvVar_DerivesFromHttpContext()
    {
        var config = new ConfigurationBuilder().Build();
        var options = Options.Create(new SpotifyOptions());
        var resolver = new HostUrlResolver(config, options);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("traefik.homelab.arpa");

        var result = resolver.ResolveBaseUrl(context);

        result.Should().Be("https://traefik.homelab.arpa");
    }

    [Fact]
    public void ResolveBaseUrl_WhenNoEnvVarAndNoContext_ReturnsLocalhostDefault()
    {
        var config = new ConfigurationBuilder().Build();
        var options = Options.Create(new SpotifyOptions());
        var resolver = new HostUrlResolver(config, options);

        var result = resolver.ResolveBaseUrl();

        result.Should().Be("http://localhost:5000");
    }

    [Fact]
    public void ResolveSpotifyRedirectUri_WhenCantusHostUrlSet_DerivesCallback()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CANTUS_HOST_URL"] = "https://cantus.myhome.net"
            })
            .Build();

        var options = Options.Create(new SpotifyOptions());
        var resolver = new HostUrlResolver(config, options);

        var result = resolver.ResolveSpotifyRedirectUri();

        result.Should().Be("https://cantus.myhome.net/api/auth/spotify/callback");
    }
}
