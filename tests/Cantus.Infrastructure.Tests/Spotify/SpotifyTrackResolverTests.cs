using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public sealed class SpotifyTrackResolverTests
{
    private const string TOKEN = "owner-token";
    private const string FILTERED_QUERY = "track:\"bohemian rhapsody\" artist:\"queen\"";
    private const string PLAIN_QUERY = "Bohemian Rhapsody - Remastered 2011 Queen, David Bowie";
    private const string TITLE = "Bohemian Rhapsody - Remastered 2011";
    private const string ARTIST = "Queen, David Bowie";
    private const int RETRY_AFTER_SECONDS = 30;

    private readonly ISpotifyCatalogClient _catalog = Substitute.For<ISpotifyCatalogClient>();
    private readonly SpotifyTrackResolver _resolver;

    public SpotifyTrackResolverTests()
    {
        _resolver = new SpotifyTrackResolver(_catalog, NullLogger<SpotifyTrackResolver>.Instance);
    }

    private static TrackInfo Track(string id, string title, string artist)
    {
        return new TrackInfo { Id = id, Title = title, Artist = artist };
    }

    private void Returns(string query, params TrackInfo[] tracks)
    {
        _catalog
            .SearchTracksAsync(TOKEN, query, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(tracks);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheFilteredSearchMatches_MakesOneSearchWithTheLimit()
    {
        TrackInfo track = Track("a", "Bohemian Rhapsody", "Queen");
        Returns(FILTERED_QUERY, track);

        TrackInfo? resolved = await _resolver.ResolveAsync(TOKEN, TITLE, ARTIST, null);

        resolved.Should().BeSameAs(track);
        await _catalog.Received(1).SearchTracksAsync(
            TOKEN,
            FILTERED_QUERY,
            SpotifyTrackResolver.SEARCH_LIMIT,
            Arg.Any<CancellationToken>());
        await _catalog.ReceivedWithAnyArgs(1).SearchTracksAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ResolveAsync_WhenTheFilteredSearchHasNoMatch_RetriesOnceWithPlainText()
    {
        TrackInfo track = Track("b", "Bohemian Rhapsody", "Queen");
        Returns(FILTERED_QUERY, Track("x", "Somebody to Love", "Queen"));
        Returns(PLAIN_QUERY, track);

        TrackInfo? resolved = await _resolver.ResolveAsync(TOKEN, TITLE, ARTIST, null);

        resolved.Should().BeSameAs(track);
        Received.InOrder(() =>
        {
            _catalog.SearchTracksAsync(TOKEN, FILTERED_QUERY, SpotifyTrackResolver.SEARCH_LIMIT, Arg.Any<CancellationToken>());
            _catalog.SearchTracksAsync(TOKEN, PLAIN_QUERY, SpotifyTrackResolver.SEARCH_LIMIT, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ResolveAsync_WhenNeitherSearchMatches_ReturnsNullAfterTwoSearches()
    {
        Returns(FILTERED_QUERY);
        Returns(PLAIN_QUERY, Track("x", "Bohemian Rhapsody", "Some Cover Band"));

        TrackInfo? resolved = await _resolver.ResolveAsync(TOKEN, TITLE, ARTIST, null);

        resolved.Should().BeNull();
        await _catalog.ReceivedWithAnyArgs(2).SearchTracksAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ResolveAsync_WhenRateLimited_StopsWithoutTheRetry()
    {
        _catalog
            .SearchTracksAsync(TOKEN, FILTERED_QUERY, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SpotifyRateLimitedException(TimeSpan.FromSeconds(RETRY_AFTER_SECONDS)));

        Func<Task> act = () => _resolver.ResolveAsync(TOKEN, TITLE, ARTIST, null);

        await act.Should().ThrowAsync<SpotifyRateLimitedException>();
        await _catalog.ReceivedWithAnyArgs(1).SearchTracksAsync(default!, default!, default, default);
    }

    [Fact]
    public void BuildQueries_WithoutAnArtist_FiltersOnTheTitleOnly()
    {
        IReadOnlyList<string> queries = SpotifyTrackResolver.BuildQueries("Yesterday", " ");

        queries.Should().Equal("track:\"yesterday\"", "Yesterday");
    }
}
