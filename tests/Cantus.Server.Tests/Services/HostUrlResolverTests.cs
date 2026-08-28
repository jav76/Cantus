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
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CANTUS_HOST_URL"] = "https://lyrics.example.com/"
            })
            .Build();

        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        string result = resolver.ResolveBaseUrl();

        result.Should().Be("https://lyrics.example.com");
    }

    [Fact]
    public void ResolveBaseUrl_WhenHostUrlAliasConfigured_ReturnsConfiguredUrl()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HOST_URL"] = "http://192.168.1.100:5000"
            })
            .Build();

        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        string result = resolver.ResolveBaseUrl();

        result.Should().Be("http://192.168.1.100:5000");
    }

    [Fact]
    public void ResolveBaseUrl_WhenNoEnvVar_DerivesFromHttpContext()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        DefaultHttpContext context = new();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("traefik.homelab.arpa");

        string result = resolver.ResolveBaseUrl(context);

        result.Should().Be("https://traefik.homelab.arpa");
    }

    [Fact]
    public void ResolveBaseUrl_WhenNoEnvVarAndNoContext_ReturnsLocalhostDefault()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        string result = resolver.ResolveBaseUrl();

        result.Should().Be("http://localhost:5000");
    }

    [Fact]
    public void ResolveSpotifyRedirectUri_WhenCantusHostUrlSet_DerivesCallback()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CANTUS_HOST_URL"] = "https://cantus.myhome.net"
            })
            .Build();

        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        string result = resolver.ResolveSpotifyRedirectUri();

        result.Should().Be("https://cantus.myhome.net/api/auth/spotify/callback");
    }

    [Fact]
    public void ResolveSpotifyRedirectUri_WhenLocalhostRequestContext_DerivesLocalhostCallback()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        DefaultHttpContext context = new();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);

        string result = resolver.ResolveSpotifyRedirectUri(context);

        result.Should().Be("http://localhost:5000/api/auth/spotify/callback");
    }

    [Fact]
    public void ResolveSpotifyRedirectUri_WhenLoopbackIpContext_DerivesLoopbackCallback()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        DefaultHttpContext context = new();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("127.0.0.1", 5000);

        string result = resolver.ResolveSpotifyRedirectUri(context);

        result.Should().Be("http://127.0.0.1:5000/api/auth/spotify/callback");
    }

    [Fact]
    public void ResolveSpotifyRedirectUri_WhenExplicitCustomProductionRedirect_ReturnsCustomRedirect()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Spotify:RedirectUri"] = "https://custom.oauth.domain/spotify/callback"
            })
            .Build();

        IOptions<SpotifyOptions> options = Options.Create(new SpotifyOptions());
        HostUrlResolver resolver = new(config, options);

        string result = resolver.ResolveSpotifyRedirectUri();

        result.Should().Be("https://custom.oauth.domain/spotify/callback");
    }
}
