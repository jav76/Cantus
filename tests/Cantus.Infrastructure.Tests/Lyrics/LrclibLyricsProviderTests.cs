using System.Net;
using System.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public class LrclibLyricsProviderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (ResponseHandler is not null)
            {
                return Task.FromResult(ResponseHandler(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task GetLyricsAsync_ExactMatchFound_ReturnsParsedSyncedLyrics()
    {
        MockHttpMessageHandler mockHandler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/get"))
                {
                    string json = """
                        {
                            "id": 100,
                            "trackName": "Bohemian Rhapsody",
                            "artistName": "Queen",
                            "albumName": "A Night at the Opera",
                            "duration": 354,
                            "instrumental": false,
                            "plainLyrics": "Is this the real life?",
                            "syncedLyrics": "[00:01.00]Is this the real life?\n[00:05.50]Is this just fantasy?"
                        }
                        """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        HttpClient httpClient = new(mockHandler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        LrclibLyricsProvider provider = new(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);

        TrackInfo track = new()
        {
            Id = "spotify_track_queen",
            Title = "Bohemian Rhapsody",
            Artist = "Queen",
            Album = "A Night at the Opera",
            Duration = TimeSpan.FromSeconds(354)
        };

        SyncedLyrics? result = await provider.GetLyricsAsync(track);

        result.Should().NotBeNull();
        result!.IsSynced.Should().BeTrue();
        result.IsInstrumental.Should().BeFalse();
        result.Lines.Should().HaveCount(2);
        result.Lines[0].Text.Should().Be("Is this the real life?");
        result.Lines[1].Text.Should().Be("Is this just fantasy?");
    }

    [Fact]
    public async Task GetLyricsAsync_WhenExact404_FallsBackToSearchAndFindsMatch()
    {
        MockHttpMessageHandler mockHandler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/get"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search"))
                {
                    string json = """
                        [
                            {
                                "id": 200,
                                "trackName": "Hotel California - Live",
                                "artistName": "Eagles",
                                "duration": 390,
                                "syncedLyrics": "[00:10.00]On a dark desert highway"
                            }
                        ]
                        """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        HttpClient httpClient = new(mockHandler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        LrclibLyricsProvider provider = new(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);

        TrackInfo track = new()
        {
            Id = "spotify_track_eagles",
            Title = "Hotel California",
            Artist = "Eagles",
            Duration = TimeSpan.FromSeconds(390)
        };

        SyncedLyrics? result = await provider.GetLyricsAsync(track);

        result.Should().NotBeNull();
        result!.Lines.Should().ContainSingle();
        result.Lines[0].Text.Should().Be("On a dark desert highway");
    }

    [Fact]
    public async Task GetLyricsAsync_WhenNotFoundAnywhere_ReturnsNull()
    {
        MockHttpMessageHandler mockHandler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        HttpClient httpClient = new(mockHandler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        LrclibLyricsProvider provider = new(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);

        TrackInfo track = new()
        {
            Id = "spotify_track_404",
            Title = "Nonexistent",
            Artist = "Ghost Artist",
            Duration = TimeSpan.FromSeconds(120)
        };

        SyncedLyrics? result = await provider.GetLyricsAsync(track);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchLyricsAsync_WhenNoMatchAnywhere_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler mockHandler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        HttpClient httpClient = new(mockHandler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        LrclibLyricsProvider provider = new(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);

        TrackInfo track = new()
        {
            Id = "spotify_track_miss",
            Title = "Nonexistent",
            Artist = "Ghost Artist",
            Duration = TimeSpan.FromSeconds(120)
        };

        LyricsFetchResult result = await provider.FetchLyricsAsync(track);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue("both endpoints answered without a match");
    }

    [Fact]
    public async Task FetchLyricsAsync_WhenLrclibReturnsServerError_ReportsUnavailable()
    {
        MockHttpMessageHandler mockHandler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };

        HttpClient httpClient = new(mockHandler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        LrclibLyricsProvider provider = new(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);

        TrackInfo track = new()
        {
            Id = "spotify_track_outage",
            Title = "Song",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(180)
        };

        LyricsFetchResult result = await provider.FetchLyricsAsync(track);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeFalse("a 5xx is a transient failure, not an authoritative miss");
    }

    [Fact]
    public async Task FetchLyricsAsync_WhenSearchFallbackFails_ReportsUnavailable()
    {
        MockHttpMessageHandler mockHandler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/get"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
        };

        HttpClient httpClient = new(mockHandler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        LrclibLyricsProvider provider = new(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);

        TrackInfo track = new()
        {
            Id = "spotify_track_search_down",
            Title = "Song",
            Artist = "Artist",
            Duration = TimeSpan.FromSeconds(180)
        };

        LyricsFetchResult result = await provider.FetchLyricsAsync(track);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeFalse("the search fallback could not be consulted");
    }

    private const string CANDIDATE_SEARCH_JSON = """
        [
            {
                "id": 501,
                "trackName": "Miss America",
                "artistName": "Brandon Flowers",
                "albumName": "The Desired Effect",
                "duration": 295,
                "instrumental": false,
                "plainLyrics": "I'm writing you this letter\nIf you ain't figured out\nThird line",
                "syncedLyrics": "[00:00.00]\n[00:08.15]I'm writing you this letter\n[00:12.10]If you ain't figured out\n[00:16.00]Third line"
            },
            {
                "id": 502,
                "trackName": "Miss America (Live)",
                "artistName": "Brandon Flowers",
                "albumName": null,
                "duration": 301.5,
                "instrumental": false,
                "plainLyrics": "\nPlain first\nPlain second\nPlain third",
                "syncedLyrics": null
            },
            {
                "id": 503,
                "trackName": "Miss America (Instrumental)",
                "artistName": "Brandon Flowers",
                "duration": 295,
                "instrumental": true,
                "plainLyrics": null,
                "syncedLyrics": null
            },
            {
                "id": 504,
                "trackName": "Miss America (Empty)",
                "artistName": "Brandon Flowers",
                "duration": 295,
                "instrumental": false,
                "plainLyrics": null,
                "syncedLyrics": null
            }
        ]
        """;

    private static readonly TrackInfo CandidateTrack = new()
    {
        Id = "spotify_miss_america",
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Duration = TimeSpan.FromSeconds(295)
    };

    private static LrclibLyricsProvider CreateProvider(MockHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        return new LrclibLyricsProvider(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);
    }

    [Fact]
    public async Task GetCandidatesAsync_ReturnsEverySearchResultThatHasLyrics()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req => req.RequestUri!.PathAndQuery.StartsWith("/api/search")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CANDIDATE_SEARCH_JSON, Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        IReadOnlyList<LyricsCandidate> candidates = await CreateProvider(handler).GetCandidatesAsync(CandidateTrack);

        candidates.Select(c => c.ProviderCandidateId).Should().Equal(
            new[] { "501", "502", "503" },
            "a result with neither synced nor plain lyrics and no instrumental flag offers nothing to pick");
        candidates.Should().OnlyContain(c => c.Provider == "LRCLIB");

        LyricsCandidate synced = candidates[0];
        synced.Title.Should().Be("Miss America");
        synced.Artist.Should().Be("Brandon Flowers");
        synced.Album.Should().Be("The Desired Effect");
        synced.Duration.Should().Be(TimeSpan.FromSeconds(295));
        synced.IsSynced.Should().BeTrue();
        synced.IsInstrumental.Should().BeFalse();
        synced.Preview.Should().Equal("I'm writing you this letter", "If you ain't figured out");

        LyricsCandidate plain = candidates[1];
        plain.IsSynced.Should().BeFalse();
        plain.Duration.Should().Be(TimeSpan.FromSeconds(301.5));
        plain.Preview.Should().Equal("Plain first", "Plain second");

        LyricsCandidate instrumental = candidates[2];
        instrumental.IsInstrumental.Should().BeTrue();
        instrumental.IsSynced.Should().BeFalse();
        instrumental.Preview.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCandidatesAsync_SearchesByTitleAndArtist()
    {
        string? requestedUrl = null;
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                requestedUrl = req.RequestUri!.PathAndQuery;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
        };

        IReadOnlyList<LyricsCandidate> candidates = await CreateProvider(handler).GetCandidatesAsync(CandidateTrack);

        candidates.Should().BeEmpty();
        requestedUrl.Should().Be("/api/search?q=Miss%20America%20Brandon%20Flowers");
    }

    [Fact]
    public async Task GetCandidatesAsync_WhenSearchFails_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };

        Func<Task> act = () => CreateProvider(handler).GetCandidatesAsync(CandidateTrack);

        await act.Should().ThrowAsync<HttpRequestException>(
            "a failed search must not read as \"LRCLIB has no candidates\"");
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_LoadsTheRecordByIdAndLabelsItWithTheTrack()
    {
        string? requestedUrl = null;
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                requestedUrl = req.RequestUri!.PathAndQuery;
                string json = """
                    {
                        "id": 501,
                        "trackName": "Miss America (Live)",
                        "artistName": "Brandon Flowers",
                        "albumName": "Live Album",
                        "duration": 301,
                        "instrumental": false,
                        "plainLyrics": "I'm writing you this letter",
                        "syncedLyrics": "[00:01.00]I'm writing you this letter\n[00:04.00]If you ain't figured out"
                    }
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(CandidateTrack, "501");

        requestedUrl.Should().Be("/api/get/501");
        lyrics.Should().NotBeNull();
        lyrics!.TrackId.Should().Be(CandidateTrack.Id);
        lyrics.Title.Should().Be("Miss America");
        lyrics.IsSynced.Should().BeTrue();
        lyrics.Lines.Select(l => l.Text).Should().Equal("I'm writing you this letter", "If you ain't figured out");
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_UnknownId_ReturnsNull()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(CandidateTrack, "999");

        lyrics.Should().BeNull();
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_NonNumericId_ReturnsNullWithoutARequest()
    {
        bool requested = false;
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ =>
            {
                requested = true;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(CandidateTrack, "../search");

        lyrics.Should().BeNull();
        requested.Should().BeFalse();
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_ServerError_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };

        Func<Task> act = () => CreateProvider(handler).GetCandidateLyricsAsync(CandidateTrack, "501");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
