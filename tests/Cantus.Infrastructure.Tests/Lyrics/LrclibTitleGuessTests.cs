using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using Cantus.Infrastructure.Lyrics;
using Cantus.Infrastructure.Lyrics.Lookup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public sealed class LrclibTitleGuessTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (ResponseHandler is not null)
            {
                return Task.FromResult(ResponseHandler(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static LrclibLyricsProvider CreateProvider(MockHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://lrclib.net") };
        IOptions<LrclibOptions> options = Options.Create(new LrclibOptions());
        return new LrclibLyricsProvider(httpClient, options, NullLogger<LrclibLyricsProvider>.Instance);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string TrackName(HttpRequestMessage request)
    {
        return HttpUtility.ParseQueryString(request.RequestUri!.Query)["track_name"]!;
    }

    private const string SEARCH_JSON = """
        [
            { "id": 1, "trackName": "Real Life", "artistName": "Queen", "albumName": "Opera", "duration": 354,
              "instrumental": false, "plainLyrics": "Is this the real life?\nIs this just fantasy?",
              "syncedLyrics": "[00:01.00]Is this the real life?" },
            { "id": 2, "trackName": "Real Life (Live)", "artistName": "Queen", "duration": 360,
              "instrumental": false, "plainLyrics": null,
              "syncedLyrics": "[00:01.00]Is this the real life?\n[00:05.50]Is this just fantasy?" },
            { "id": 3, "trackName": "Real Life (Instrumental)", "artistName": "Queen",
              "instrumental": true, "plainLyrics": null, "syncedLyrics": null },
            { "id": 4, "trackName": "Real Life (Empty)", "artistName": "Queen",
              "instrumental": false, "plainLyrics": "  ", "syncedLyrics": null }
        ]
        """;

    [Fact]
    public async Task SearchByFragmentAsync_SendsEachGuessAsTrackNameTaggedAsLookup()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json("[]") };
        LrclibLyricsProvider provider = CreateProvider(handler);

        await provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life", "this the real"]));

        handler.Requests.Should().HaveCount(2);
        foreach (HttpRequestMessage request in handler.Requests)
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/search");
            NameValueCollection parameters = HttpUtility.ParseQueryString(request.RequestUri.Query);
            parameters.AllKeys.Should().Equal("track_name");
            parameters["q"].Should().BeNull();
            ProviderRequestOptions.IsLookupRequest(request).Should().BeTrue();
        }

        handler.Requests.Select(TrackName).Should().Equal("real life", "this the real");
    }

    [Fact]
    public async Task SearchByFragmentAsync_ManyGuesses_SendsAtMostTheCap()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json("[]") };
        LrclibLyricsProvider provider = CreateProvider(handler);

        await provider.SearchByFragmentAsync(new LyricsFragmentQuery(
            "a b c d e",
            ["one two", "ONE TWO", " ", "three four", "five six", "seven eight"]));

        handler.Requests.Should().HaveCount(LrclibLyricsProvider.MAX_TITLE_GUESSES);
        handler.Requests.Select(TrackName).Should().Equal("one two", "three four");
    }

    [Fact]
    public async Task SearchByFragmentAsync_NoGuesses_SendsNoRequest()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(SEARCH_JSON) };
        LrclibLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits =
            await provider.SearchByFragmentAsync(new LyricsFragmentQuery("is this the real life", []));

        hits.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByFragmentAsync_MapsRecordsToPlainOrUntimedLyrics()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(SEARCH_JSON) };
        LrclibLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits =
            await provider.SearchByFragmentAsync(new LyricsFragmentQuery("is this the real life", ["real life"]));

        hits.Select(h => h.Candidate.ProviderCandidateId).Should().Equal("1", "2");
        hits[0].SearchableText.Should().Be("Is this the real life?\nIs this just fantasy?");
        hits[1].SearchableText.Should().Be("Is this the real life?\nIs this just fantasy?");
        hits[1].SearchableText.Should().NotContain("[00:");
        hits[0].Candidate.Provider.Should().Be(LrclibLyricsProvider.PROVIDER_NAME);
        hits[0].Candidate.Title.Should().Be("Real Life");
        hits[0].Candidate.Artist.Should().Be("Queen");
        hits[0].Candidate.Duration.Should().Be(TimeSpan.FromSeconds(354));
    }

    [Fact]
    public async Task SearchByFragmentAsync_SameRecordFromTwoGuesses_IsReturnedOnce()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(SEARCH_JSON) };
        LrclibLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life", "the real life"]));

        handler.Requests.Should().HaveCount(2);
        hits.Select(h => h.Candidate.ProviderCandidateId).Should().Equal("1", "2");
    }

    [Fact]
    public async Task SearchByFragmentAsync_FirstRequestFails_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        };
        LrclibLyricsProvider provider = CreateProvider(handler);

        Func<Task> search = () => provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life", "this the real"]));

        await search.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchByFragmentAsync_LaterRequestFails_KeepsEarlierHits()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req => TrackName(req) == "real life"
                ? Json(SEARCH_JSON)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };
        LrclibLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life", "this the real"]));

        hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchByFragmentAsync_CancelledDuringALaterRequest_KeepsEarlierHits()
    {
        using CancellationTokenSource deadline = new();
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (TrackName(req) == "real life")
                {
                    return Json(SEARCH_JSON);
                }

                deadline.Cancel();
                throw new OperationCanceledException(deadline.Token);
            }
        };
        LrclibLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life", "this the real"]),
            deadline.Token);

        hits.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchByFragmentAsync_CancelledDuringTheFirstRequest_Throws()
    {
        using CancellationTokenSource deadline = new();
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ =>
            {
                deadline.Cancel();
                throw new OperationCanceledException(deadline.Token);
            }
        };
        LrclibLyricsProvider provider = CreateProvider(handler);

        Func<Task> search = () => provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life", "this the real"]),
            deadline.Token);

        await search.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchByFragmentAsync_OversizedResponse_Throws()
    {
        string padding = new('x', ProviderResponseReader.MAX_LOOKUP_RESPONSE_BYTES);
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json($$"""[ { "id": 1, "plainLyrics": "{{padding}}" } ]""")
        };
        LrclibLyricsProvider provider = CreateProvider(handler);

        Func<Task> search = () => provider.SearchByFragmentAsync(
            new LyricsFragmentQuery("is this the real life", ["real life"]));

        await search.Should().ThrowAsync<HttpRequestException>();
    }
}
