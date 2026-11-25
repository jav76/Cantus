using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.CoverArt;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cantus.Infrastructure.Tests.CoverArt;

public class MusicBrainzCoverArtCatalogTests
{
    private const string ALBUM_GROUP = "11111111-1111-4111-8111-111111111111";
    private const string SINGLE_GROUP = "22222222-2222-4222-8222-222222222222";

    private static readonly TrackInfo Track = new()
    {
        Id = "local:0123456789abcdef0123456789abcdef",
        Title = "Don't Stop Me Now",
        Artist = "Queen",
        Duration = TimeSpan.FromSeconds(210)
    };

    private readonly StubHttpMessageHandler _musicBrainz = new();
    private readonly StubHttpMessageHandler _archive = new();

    public MusicBrainzCoverArtCatalogTests()
    {
        _musicBrainz.ResponseHandler = _ => Json(SearchResponse(
            Release(SINGLE_GROUP, "Don't Stop Me Now", "Single"),
            Release(ALBUM_GROUP, "Jazz", "Album")));
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenTheBestReleaseGroupHasACover_ReturnsItsUrl()
    {
        _archive.ResponseHandler = _ => Image();

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.Found(ArtUrl(ALBUM_GROUP)));
        _archive.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenTheBestReleaseGroupHasNoCover_TriesTheNext()
    {
        _archive.ResponseHandler = request => request.RequestUri!.AbsolutePath.Contains(ALBUM_GROUP)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Image();

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.Found(ArtUrl(SINGLE_GROUP)));
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenNoReleaseGroupHasACover_IsADefinitiveMiss()
    {
        _archive.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.NotFound());
        _archive.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenNoRecordingMatchesConfidently_IsADefinitiveMissWithoutAskingTheArchive()
    {
        _musicBrainz.ResponseHandler = _ => Json(SearchResponseBy("A Tribute Band", Release(ALBUM_GROUP, "Jazz", "Album")));

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.NotFound());
        _archive.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchCoverArtAsync_ProbesAtMostTheMaximumNumberOfReleaseGroups()
    {
        _musicBrainz.ResponseHandler = _ => Json(SearchResponse(Enumerable
            .Range(0, MusicBrainzCoverArtCatalog.MAX_RELEASE_GROUPS_PROBED + 2)
            .Select(i => Release($"0000000{i}-0000-4000-8000-000000000000", "Jazz", "Album"))
            .ToArray()));
        _archive.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        await CreateCatalog().FetchCoverArtAsync(Track);

        _archive.Requests.Should().HaveCount(MusicBrainzCoverArtCatalog.MAX_RELEASE_GROUPS_PROBED);
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenMusicBrainzIsBusy_IsUnavailable()
    {
        _musicBrainz.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.Unavailable());
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhileRequestsArePaused_IsUnavailable()
    {
        _musicBrainz.ResponseHandler = _ =>
            throw new ProviderRateLimitedException(MusicBrainzClient.PROVIDER_NAME, TimeSpan.FromSeconds(30));

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.Unavailable());
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenTheArchiveFails_IsUnavailableNotAMiss()
    {
        _archive.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.Unavailable());
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenARequestTimesOut_IsUnavailable()
    {
        _musicBrainz.ResponseHandler = _ => throw new TaskCanceledException("The request timed out.");

        CoverArtFetchResult result = await CreateCatalog().FetchCoverArtAsync(Track);

        result.Should().Be(CoverArtFetchResult.Unavailable());
    }

    [Fact]
    public async Task FetchCoverArtAsync_WhenTheCallerCancels_Throws()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> act = () => CreateCatalog().FetchCoverArtAsync(Track, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private MusicBrainzCoverArtCatalog CreateCatalog() => new(
        new MusicBrainzClient(new HttpClient(_musicBrainz) { BaseAddress = new Uri("https://musicbrainz.test") }),
        new CoverArtArchiveClient(new HttpClient(_archive) { BaseAddress = new Uri("https://coverartarchive.test") }),
        NullLogger<MusicBrainzCoverArtCatalog>.Instance);

    private static string ArtUrl(string releaseGroupId) =>
        $"https://coverartarchive.test/release-group/{releaseGroupId}/front-500";

    private static string Release(string releaseGroupId, string title, string primaryType) =>
        $$"""
        {
          "title": "{{title}}",
          "status": "Official",
          "date": "1978",
          "release-group": { "id": "{{releaseGroupId}}", "primary-type": "{{primaryType}}" }
        }
        """;

    private static string SearchResponse(params string[] releases) => SearchResponseBy("Queen", releases);

    private static string SearchResponseBy(string artist, params string[] releases) =>
        $$"""
        {
          "recordings": [
            {
              "title": "Don’t Stop Me Now",
              "length": 210133,
              "artist-credit": [ { "name": "{{artist}}", "artist": { "name": "{{artist}}" } } ],
              "releases": [ {{string.Join(",", releases)}} ]
            }
          ]
        }
        """;

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Image()
    {
        ByteArrayContent content = new([]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
