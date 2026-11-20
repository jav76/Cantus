using System.Net;
using Cantus.Core.Models;
using Cantus.Infrastructure;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Infrastructure.Tests.Configuration;

public sealed class LyricsProviderRegistrationTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

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
    public async Task TypedClients_SendThroughTheSharedProviderRateGate()
    {
        CountingHandler primary = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCantusInfrastructure(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => primary));
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        ProviderRateGate gate = provider.GetRequiredService<ProviderRateGate>();
        gate.ReportResponse(LrclibLyricsProvider.PROVIDER_NAME, false, HttpStatusCode.TooManyRequests, null);
        gate.ReportResponse(NeteaseLyricsProvider.PROVIDER_NAME, false, HttpStatusCode.TooManyRequests, null);
        TrackInfo track = new() { Id = "t1", Title = "Title", Artist = "Artist" };

        LyricsFetchResult lrclib = await scope.ServiceProvider
            .GetRequiredService<LrclibLyricsProvider>()
            .FetchLyricsAsync(track);
        LyricsFetchResult netease = await scope.ServiceProvider
            .GetRequiredService<NeteaseLyricsProvider>()
            .FetchLyricsAsync(track);

        lrclib.Should().Be(LyricsFetchResult.Unavailable());
        netease.Should().Be(LyricsFetchResult.Unavailable());
        primary.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("500")]
    public void SongLookupOptions_WhenBudgetOutOfRange_FailsStartupValidation(string requestsPerMinute)
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SongLookup:LrclibRequestsPerMinute"] = requestsPerMinute,
        });

        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        validate.Should().Throw<OptionsValidationException>()
            .Which.OptionsType.Should().Be<SongLookupOptions>();
    }

    [Fact]
    public void SongLookupOptions_ByDefault_PassStartupValidation()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>());

        Action validate = () => provider.GetRequiredService<IStartupValidator>().Validate();

        validate.Should().NotThrow();
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
