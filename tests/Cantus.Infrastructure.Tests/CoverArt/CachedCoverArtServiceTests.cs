using Cantus.Core.Models;
using Cantus.Infrastructure.CoverArt;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cantus.Infrastructure.Tests.CoverArt;

public class CachedCoverArtServiceTests
{
    private const string TRACK_ID = "local:0123456789abcdef0123456789abcdef";
    private const string ART_URL = "https://coverartarchive.org/release-group/9162580e-5df4-32de-80cc-f45a8d8a9b1d/front-500";
    private const int CACHE_DAYS = 60;
    private const int NEGATIVE_CACHE_DAYS = 7;

    private static readonly TrackInfo Track = new()
    {
        Id = TRACK_ID,
        Title = "Come Together",
        Artist = "The Beatles",
        Duration = TimeSpan.FromSeconds(260)
    };

    private readonly ICoverArtCacheRepository _cache = Substitute.For<ICoverArtCacheRepository>();
    private readonly ICoverArtCatalog _catalog = Substitute.For<ICoverArtCatalog>();
    private readonly CoverArtOptions _options = new() { CacheDays = CACHE_DAYS, NegativeCacheDays = NEGATIVE_CACHE_DAYS };

    public CachedCoverArtServiceTests()
    {
        _catalog.CatalogName.Returns("Catalogue");
    }

    [Fact]
    public async Task GetCachedAsync_WithACachedCover_ReturnsIt()
    {
        _cache.GetAsync(TRACK_ID, Arg.Any<CancellationToken>()).Returns(new CoverArtCacheEntry(ART_URL));

        CoverArtLookup lookup = await CreateService().GetCachedAsync(Track);

        lookup.Should().Be(CoverArtLookup.Found(ART_URL));
    }

    [Fact]
    public async Task GetCachedAsync_WithACachedMiss_ReturnsNotFound()
    {
        _cache.GetAsync(TRACK_ID, Arg.Any<CancellationToken>()).Returns(new CoverArtCacheEntry(null));

        CoverArtLookup lookup = await CreateService().GetCachedAsync(Track);

        lookup.Should().Be(CoverArtLookup.NotFound());
    }

    [Fact]
    public async Task GetCachedAsync_WithNoEntry_IsUnresolvedAndNeverAsksTheCatalogue()
    {
        CoverArtLookup lookup = await CreateService().GetCachedAsync(Track);

        lookup.Should().Be(CoverArtLookup.Unresolved());
        await _catalog.DidNotReceiveWithAnyArgs().FetchCoverArtAsync(default!, default);
    }

    [Fact]
    public async Task GetCachedAsync_WhenTheCacheFails_IsUnresolvedInsteadOfThrowing()
    {
        _cache.GetAsync(TRACK_ID, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("locked"));

        CoverArtLookup lookup = await CreateService().GetCachedAsync(Track);

        lookup.Should().Be(CoverArtLookup.Unresolved());
    }

    [Theory]
    [InlineData(ART_URL)]
    [InlineData(null)]
    public async Task ResolveAsync_WithACachedAnswer_ReturnsItWithoutAskingTheCatalogue(string? cachedArtUrl)
    {
        _cache.GetAsync(TRACK_ID, Arg.Any<CancellationToken>()).Returns(new CoverArtCacheEntry(cachedArtUrl));

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.ArtUrl.Should().Be(cachedArtUrl);
        lookup.Status.Should().NotBe(CoverArtLookupStatus.Unresolved);
        await _catalog.DidNotReceiveWithAnyArgs().FetchCoverArtAsync(default!, default);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheCatalogueFindsACover_CachesItForTheCacheDays()
    {
        _catalog.FetchCoverArtAsync(Track, Arg.Any<CancellationToken>()).Returns(CoverArtFetchResult.Found(ART_URL));

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.Should().Be(CoverArtLookup.Found(ART_URL));
        await _cache.Received(1).SaveAsync(TRACK_ID, ART_URL, TimeSpan.FromDays(CACHE_DAYS), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenTheCatalogueHasNoMatch_CachesTheMissForTheNegativeCacheDays()
    {
        _catalog.FetchCoverArtAsync(Track, Arg.Any<CancellationToken>()).Returns(CoverArtFetchResult.NotFound());

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.Should().Be(CoverArtLookup.NotFound());
        await _cache.Received(1).SaveAsync(
            TRACK_ID,
            null,
            TimeSpan.FromDays(NEGATIVE_CACHE_DAYS),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenTheCatalogueIsUnavailable_CachesNothing()
    {
        _catalog.FetchCoverArtAsync(Track, Arg.Any<CancellationToken>()).Returns(CoverArtFetchResult.Unavailable());

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.Should().Be(CoverArtLookup.Unresolved());
        await _cache.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheCatalogueThrows_IsUnresolvedAndCachesNothing()
    {
        _catalog.FetchCoverArtAsync(Track, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("boom"));

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.Should().Be(CoverArtLookup.Unresolved());
        await _cache.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ResolveAsync_WhenSavingFails_StillReturnsTheCover()
    {
        _catalog.FetchCoverArtAsync(Track, Arg.Any<CancellationToken>()).Returns(CoverArtFetchResult.Found(ART_URL));
        _cache.SaveAsync(default!, default, default, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("locked"));

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.Should().Be(CoverArtLookup.Found(ART_URL));
    }

    [Fact]
    public async Task ResolveAsync_WhenTurnedOff_NeverAsksTheCatalogue()
    {
        _options.Enabled = false;

        CoverArtLookup lookup = await CreateService().ResolveAsync(Track);

        lookup.Should().Be(CoverArtLookup.Unresolved());
        await _catalog.DidNotReceiveWithAnyArgs().FetchCoverArtAsync(default!, default);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheCallerCancels_Throws()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        _catalog.FetchCoverArtAsync(Track, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        Func<Task> act = () => CreateService().ResolveAsync(Track, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private CachedCoverArtService CreateService() => new(
        _cache,
        _catalog,
        Options.Create(_options),
        NullLogger<CachedCoverArtService>.Instance);
}
