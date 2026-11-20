using System.Net;
using System.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public class NeteaseLyricsProviderTests
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

    private static NeteaseLyricsProvider CreateProvider(MockHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://music.163.com") };
        IOptions<NeteaseOptions> options = Options.Create(new NeteaseOptions());
        return new NeteaseLyricsProvider(httpClient, options, NullLogger<NeteaseLyricsProvider>.Instance);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string SEARCH_JSON = """
        {
            "result": {
                "songs": [
                    { "id": 111, "name": "Miss America", "duration": 295011,
                      "artists": [ { "name": "Brandon Flowers" } ] },
                    { "id": 222, "name": "Miss America", "duration": 244000,
                      "artists": [ { "name": "Carolina Liar" } ] }
                ],
                "songCount": 2
            },
            "code": 200
        }
        """;

    private static readonly TrackInfo MatchingTrack = new()
    {
        Id = "spotify_miss_america",
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Duration = TimeSpan.FromMilliseconds(295011)
    };

    [Fact]
    public async Task FetchLyricsAsync_MatchFound_ParsesSyncedLyricsAndStripsCreditLines()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                if (req.RequestUri!.PathAndQuery.StartsWith("/api/song/lyric") &&
                    req.RequestUri!.Query.Contains("id=111"))
                {
                    return Json("""
                        {
                            "lrc": { "lyric": "[00:00.000] 作词 : Brandon Flowers\n[00:01.000] 作曲 : Brandon Flowers\n[00:08.156]I'm writing you this letter\n[00:12.105]If you ain't figured out\n" },
                            "code": 200
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.IsDefinitive.Should().BeTrue();
        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsSynced.Should().BeTrue();
        result.Lyrics.Lines.Should().HaveCount(2, "the two credit lines must be stripped");
        result.Lyrics.Lines[0].Text.Should().Be("I'm writing you this letter");
        result.Lyrics.Lines[1].Text.Should().Be("If you ain't figured out");
    }

    [Fact]
    public async Task FetchLyricsAsync_NoDurationOrArtistMatch_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req => req.RequestUri!.PathAndQuery.StartsWith("/api/search/get")
                ? Json(SEARCH_JSON)
                : new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        TrackInfo wrongDuration = new()
        {
            Id = "t-wrong",
            Title = "Miss America",
            Artist = "Brandon Flowers",
            Duration = TimeSpan.FromSeconds(180)
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(wrongDuration);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue("the search answered; no candidate matched");
    }

    [Fact]
    public async Task FetchLyricsAsync_UncollectedSong_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "uncollected": true, "code": 200 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue();
    }

    [Fact]
    public async Task FetchLyricsAsync_InstrumentalSong_ReportsInstrumentalLyrics()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "nolyric": true, "code": 200 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsInstrumental.Should().BeTrue();
        result.Lyrics.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchLyricsAsync_SearchPayloadErrorCode_ReportsUnavailable()
    {
        // NetEase signals rate limits and anti-scraping challenges with an
        // HTTP 200 whose payload carries a non-200 "code" (e.g. -460). That
        // must never read as an authoritative miss, or the track would be
        // negative-cached for the full TTL.
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "code": -460, "message": "Cheating" }""")
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeFalse("a payload error code is transient, not an authoritative miss");
    }

    [Fact]
    public async Task FetchLyricsAsync_LyricPayloadErrorCode_ReportsUnavailable()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "code": 500 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeFalse();
    }

    [Fact]
    public async Task FetchLyricsAsync_StripsAbbreviatedAndEnglishCreditVariants()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""
                    {
                        "lrc": { "lyric": "[00:00.000] 词: Someone\n[00:01.000] 曲: Someone Else\n[00:01.500] 词/曲: A Third Person\n[00:02.000] Written by: An English Credit\n[00:03.000]Produced by: Another Credit\n[00:08.156]The actual first lyric\n" },
                        "code": 200
                    }
                    """);
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.Lines.Should().HaveCount(1, "all five credit-line variants must be stripped");
        result.Lyrics.Lines[0].Text.Should().Be("The actual first lyric");
    }

    [Fact]
    public async Task FetchLyricsAsync_ShortArtistSubstring_DoesNotFalselyMatch()
    {
        // "Steve".Contains("Eve") must not match: substring matches require a
        // minimum artist-name length, otherwise short names pick wrong songs.
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req => req.RequestUri!.PathAndQuery.StartsWith("/api/search/get")
                ? Json("""
                    {
                        "result": {
                            "songs": [
                                { "id": 333, "name": "Some Song", "duration": 200000,
                                  "artists": [ { "name": "Eve" } ] }
                            ],
                            "songCount": 1
                        },
                        "code": 200
                    }
                    """)
                : new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        TrackInfo track = new()
        {
            Id = "t-steve",
            Title = "Some Song",
            Artist = "Steve",
            Duration = TimeSpan.FromMilliseconds(200000)
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(track);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue("the search answered; no candidate matched");
    }

    [Fact]
    public async Task FetchLyricsAsync_JoinedSpotifyArtists_MatchIndividualNeteaseArtist()
    {
        // Spotify joins artists ("Kendrick Lamar, SZA"); NetEase lists them
        // individually. Tokenized comparison must still connect the two.
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json("""
                        {
                            "result": {
                                "songs": [
                                    { "id": 444, "name": "All The Stars", "duration": 232000,
                                      "artists": [ { "name": "SZA" } ] }
                                ],
                                "songCount": 1
                            },
                            "code": 200
                        }
                        """);
                }

                return Json("""
                    { "lrc": { "lyric": "[00:05.000]This may be the night\n" }, "code": 200 }
                    """);
            }
        };

        TrackInfo track = new()
        {
            Id = "t-allthestars",
            Title = "All The Stars",
            Artist = "Kendrick Lamar, SZA",
            Duration = TimeSpan.FromMilliseconds(232000)
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(track);

        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task FetchLyricsAsync_ServerError_ReportsUnavailable()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeFalse("a 5xx is transient, not an authoritative miss");
    }

    [Fact]
    public async Task FetchLyricsAsync_LyricsWithoutTimestamps_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "lrc": { "lyric": "just plain text with no timestamps" }, "code": 200 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue("this provider is synced-only by design");
    }

    private const string CANDIDATE_SEARCH_JSON = """
        {
            "result": {
                "songs": [
                    { "id": 111, "name": "Miss America", "duration": 295011,
                      "artists": [ { "name": "Brandon Flowers" } ],
                      "album": { "name": "The Desired Effect" } },
                    { "id": 222, "name": "Miss America", "duration": 244000,
                      "artists": [ { "name": "Carolina Liar" } ] },
                    { "id": 333, "name": "Miss America (Live)", "duration": 310000,
                      "artists": [ { "name": "Brandon Flowers" }, { "name": "Guest" } ] },
                    { "id": 444, "name": "Miss America (Demo)", "duration": 295000,
                      "artists": [ { "name": "Brandon Flowers" } ] },
                    { "id": 555, "name": "Miss America (Instrumental)", "duration": 295000,
                      "artists": [ { "name": "Brandon Flowers" } ] }
                ]
            },
            "code": 200
        }
        """;

    private static HttpResponseMessage CandidateLyricResponse(string query)
    {
        if (query.Contains("id=111"))
        {
            return Json("""
                {
                    "lrc": { "lyric": "[00:00.000] 作词 : Brandon Flowers\n[00:08.156]I'm writing you this letter\n[00:12.105]If you ain't figured out\n[00:16.000]Third line\n" },
                    "code": 200
                }
                """);
        }

        if (query.Contains("id=333"))
        {
            return Json("""{ "lrc": { "lyric": "[00:05.000]Live first\n[00:09.000]Live second\n" }, "code": 200 }""");
        }

        if (query.Contains("id=444"))
        {
            return Json("""{ "uncollected": true, "code": 200 }""");
        }

        if (query.Contains("id=555"))
        {
            return Json("""{ "nolyric": true, "code": 200 }""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCandidatesAsync_ReturnsSyncedAndInstrumentalCandidatesForMatchingArtists()
    {
        List<string> lyricRequests = new();
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(CANDIDATE_SEARCH_JSON);
                }

                lock (lyricRequests)
                {
                    lyricRequests.Add(req.RequestUri!.Query);
                }

                return CandidateLyricResponse(req.RequestUri!.Query);
            }
        };

        IReadOnlyList<LyricsCandidate> candidates = await CreateProvider(handler).GetCandidatesAsync(MatchingTrack);

        candidates.Select(c => c.ProviderCandidateId).Should().Equal(
            new[] { "111", "333", "555" },
            "other artists are filtered out and songs without lyrics are skipped, in search order");
        lyricRequests.Should().NotContain(q => q.Contains("id=222"), "a different artist is never looked up");
        candidates.Should().OnlyContain(c => c.Provider == "NetEase");

        LyricsCandidate best = candidates[0];
        best.Title.Should().Be("Miss America");
        best.Artist.Should().Be("Brandon Flowers");
        best.Album.Should().Be("The Desired Effect");
        best.Duration.Should().Be(TimeSpan.FromMilliseconds(295011));
        best.IsSynced.Should().BeTrue();
        best.IsInstrumental.Should().BeFalse();
        best.Preview.Should().Equal(
            new[] { "I'm writing you this letter", "If you ain't figured out" },
            "credit lines are stripped before the preview is taken");

        LyricsCandidate live = candidates[1];
        live.Artist.Should().Be("Brandon Flowers, Guest");
        live.Duration.Should().Be(TimeSpan.FromSeconds(310), "duration does not filter candidates");
        live.Preview.Should().Equal("Live first", "Live second");

        LyricsCandidate instrumental = candidates[2];
        instrumental.IsInstrumental.Should().BeTrue();
        instrumental.IsSynced.Should().BeFalse();
        instrumental.Preview.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCandidatesAsync_LooksUpAtMostFiveSongs()
    {
        string songs = string.Join(
            ',',
            Enumerable.Range(1, 8).Select(i =>
                $$"""{ "id": {{i}}, "name": "Miss America", "duration": 295000, "artists": [ { "name": "Brandon Flowers" } ] }"""));
        int lyricRequestCount = 0;
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json($$"""{ "result": { "songs": [ {{songs}} ] }, "code": 200 }""");
                }

                Interlocked.Increment(ref lyricRequestCount);
                return Json("""{ "lrc": { "lyric": "[00:01.000]Line\n" }, "code": 200 }""");
            }
        };

        IReadOnlyList<LyricsCandidate> candidates = await CreateProvider(handler).GetCandidatesAsync(MatchingTrack);

        candidates.Should().HaveCount(5);
        lyricRequestCount.Should().Be(5, "each candidate costs one request to an unofficial API");
    }

    [Fact]
    public async Task GetCandidatesAsync_SearchPayloadError_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "code": -460, "message": "Cheating" }""")
        };

        Func<Task> act = () => CreateProvider(handler).GetCandidatesAsync(MatchingTrack);

        await act.Should().ThrowAsync<HttpRequestException>(
            "an anti-scraping challenge must not read as \"NetEase has no candidates\"");
    }

    [Fact]
    public async Task GetCandidatesAsync_LyricPayloadError_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req => req.RequestUri!.PathAndQuery.StartsWith("/api/search/get")
                ? Json(CANDIDATE_SEARCH_JSON)
                : Json("""{ "code": -460 }""")
        };

        Func<Task> act = () => CreateProvider(handler).GetCandidatesAsync(MatchingTrack);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetCandidatesAsync_OneLyricLookupFails_KeepsTheSongsThatSucceeded(HttpStatusCode failure)
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(CANDIDATE_SEARCH_JSON);
                }

                if (req.RequestUri!.Query.Contains("id=333"))
                {
                    return failure == HttpStatusCode.OK
                        ? Json("""{ "code": -460 }""")
                        : new HttpResponseMessage(failure);
                }

                return CandidateLyricResponse(req.RequestUri!.Query);
            }
        };

        IReadOnlyList<LyricsCandidate> candidates = await CreateProvider(handler).GetCandidatesAsync(MatchingTrack);

        candidates.Select(c => c.ProviderCandidateId).Should().Equal(
            new[] { "111", "555" },
            "one failed lookup must not hide the candidates that were found");
    }

    [Fact]
    public async Task GetCandidatesAsync_LookupsFailAndNoneFindLyrics_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(CANDIDATE_SEARCH_JSON);
                }

                return req.RequestUri!.Query.Contains("id=444")
                    ? Json("""{ "uncollected": true, "code": 200 }""")
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
        };

        Func<Task> act = () => CreateProvider(handler).GetCandidatesAsync(MatchingTrack);

        await act.Should().ThrowAsync<HttpRequestException>(
            "an empty list would read as \"NetEase has no candidates\" when it could not answer");
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_LoadsTheSongLyricAndLabelsItWithTheTrack()
    {
        string? requestedUrl = null;
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                requestedUrl = req.RequestUri!.PathAndQuery;
                return Json("""
                    {
                        "lrc": { "lyric": "[00:00.00]作词 : Brandon Flowers\n[00:01.00]I'm writing you this letter" },
                        "code": 200
                    }
                    """);
            }
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(MatchingTrack, "111");

        requestedUrl.Should().StartWith("/api/song/lyric?id=111&");
        lyrics.Should().NotBeNull();
        lyrics!.TrackId.Should().Be(MatchingTrack.Id);
        lyrics.Lines.Select(l => l.Text).Should().Equal("I'm writing you this letter");
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_InstrumentalSong_ReturnsInstrumentalLyrics()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "nolyric": true, "code": 200 }""")
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(MatchingTrack, "333");

        lyrics.Should().NotBeNull();
        lyrics!.IsInstrumental.Should().BeTrue();
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_UncollectedSong_ReturnsNull()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "uncollected": true, "code": 200 }""")
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(MatchingTrack, "444");

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
                return Json("""{ "uncollected": true, "code": 200 }""");
            }
        };

        SyncedLyrics? lyrics = await CreateProvider(handler).GetCandidateLyricsAsync(MatchingTrack, "abc");

        lyrics.Should().BeNull();
        requested.Should().BeFalse();
    }

    [Fact]
    public async Task GetCandidateLyricsAsync_PayloadError_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "code": -460, "message": "Cheating" }""")
        };

        Func<Task> act = () => CreateProvider(handler).GetCandidateLyricsAsync(MatchingTrack, "111");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
