using Cantus.Infrastructure;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cantus.Infrastructure.Tests.Configuration;

public sealed class LyricsProviderRegistrationTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCantusInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ProviderChain_ByDefault_IsLrclibThenNetease()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>());
        using IServiceScope scope = provider.CreateScope();

        IReadOnlyList<ILyricsFetchProvider> chain =
            scope.ServiceProvider.GetRequiredService<IReadOnlyList<ILyricsFetchProvider>>();

        chain.Select(p => p.ProviderName).Should().Equal("LRCLIB", "NetEase");
        scope.ServiceProvider.GetService<LyricsCandidateService>().Should().NotBeNull();
    }

    [Fact]
    public void ProviderChain_WhenNeteaseDisabled_LeavesNeteaseOutOfLookupsAndCandidates()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Netease:Enabled"] = "false",
        });
        using IServiceScope scope = provider.CreateScope();

        IReadOnlyList<ILyricsFetchProvider> chain =
            scope.ServiceProvider.GetRequiredService<IReadOnlyList<ILyricsFetchProvider>>();

        chain.Select(p => p.ProviderName).Should().Equal("LRCLIB");
        scope.ServiceProvider.GetService<LyricsCandidateService>().Should().NotBeNull(
            "candidates are gathered from this same chain");
    }
}
