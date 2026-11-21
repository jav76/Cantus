using Cantus.Core.Interfaces;
using Cantus.Infrastructure.Lyrics.Lookup;
using Cantus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cantus.Infrastructure.Tests.Configuration;

public sealed class LyricsSearchRegistrationTests
{
    private static ServiceProvider BuildValidatedProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddCantusInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public void Container_WithScopeValidation_BuildsAndResolvesTheLookupServices()
    {
        using ServiceProvider provider = BuildValidatedProvider();
        using IServiceScope scope = provider.CreateScope();

        ILyricsCorpusReader reader = scope.ServiceProvider.GetRequiredService<ILyricsCorpusReader>();

        reader.Should().BeOfType<TraceLoggingLyricsCorpusReaderDecorator>();
        provider.GetRequiredService<LyricsSearchIndex>()
            .Should().BeSameAs(provider.GetRequiredService<LyricsSearchIndex>());
    }

    [Fact]
    public void Container_ResolvesTheLookupServiceThroughItsTraceDecorator()
    {
        using ServiceProvider provider = BuildValidatedProvider();
        using IServiceScope scope = provider.CreateScope();

        ISongLookupService service = scope.ServiceProvider.GetRequiredService<ISongLookupService>();

        service.Should().BeOfType<TraceLoggingSongLookupServiceDecorator>();
        provider.GetRequiredService<SongLookupBudget>()
            .Should().BeSameAs(provider.GetRequiredService<SongLookupBudget>());
        provider.GetRequiredService<SongLookupQueryCache>()
            .Should().BeSameAs(provider.GetRequiredService<SongLookupQueryCache>());
    }

    [Fact]
    public void DbContext_HasTheSingletonChangeInterceptorAttached()
    {
        using ServiceProvider provider = BuildValidatedProvider();
        using IServiceScope scope = provider.CreateScope();

        CantusDbContext context = scope.ServiceProvider.GetRequiredService<CantusDbContext>();
        CoreOptionsExtension? core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        core.Should().NotBeNull();
        core!.Interceptors.Should().Contain(provider.GetRequiredService<LyricsCorpusChangeInterceptor>());
    }
}
