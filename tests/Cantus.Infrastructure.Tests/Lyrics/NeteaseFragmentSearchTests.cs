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

public sealed class NeteaseFragmentSearchTests
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

    private static NeteaseLyricsProvider CreateProvider(MockHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://music.163.com") };
        IOptions<NeteaseOptions> options = Options.Create(new NeteaseOptions());
        return new NeteaseLyricsProvider(httpClient, options, NullLogger<NeteaseLyricsProvider>.Instance);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        // NetEase labels its JSON as text/plain.
        Content = new StringContent(json, Encoding.UTF8, "text/plain")
    };

    private static LyricsFragmentQuery Query(string text) => new(text, []);

    private const string OBJECT_SHAPE_JSON = """
        {
            "result": {
                "songs": [
                    { "id": 1859245776, "name": "Bohemian Rhapsody", "duration": 354000,
                      "artists": [ { "name": "Queen" } ], "album": { "name": "A Night at the Opera" },
                      "lyrics": { "txt": "Is this the real life?\nIs this just fantasy?",
                                  "range": [ { "first": 0, "second": 21 } ] } }
                ],
                "songCount": 60
            },
            "code": 200
        }
        """;

    private const string ARRAY_SHAPE_JSON = """
        {
            "result": {
                "songs": [
                    { "id": 42, "name": "Song", "duration": 200000, "artists": [ { "name": "A" }, { "name": "B" } ],
                      "lyrics": [ "Is <b>this</b> the <B>real</B> life?", "", "Is this just fantasy?" ] }
                ]
            },
            "code": 200
        }
        """;

    [Fact]
    public async Task SearchByFragmentAsync_SendsOneLyricSearchTaggedAsLookup()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(OBJECT_SHAPE_JSON) };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        await provider.SearchByFragmentAsync(Query("  is this the real life  "));

        handler.Requests.Should().ContainSingle();
        HttpRequestMessage request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsolutePath.Should().Be("/api/search/get");
        NameValueCollection parameters = HttpUtility.ParseQueryString(request.RequestUri.Query);
        parameters["s"].Should().Be("is this the real life");
        parameters["type"].Should().Be("1006");
        parameters["limit"].Should().Be("10");
        ProviderRequestOptions.IsLookupRequest(request).Should().BeTrue();
    }

    [Fact]
    public async Task SearchByFragmentAsync_ObjectExcerpt_MapsSongAndExcerptText()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(OBJECT_SHAPE_JSON) };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(Query("is this the real life"));

        LyricsFragmentHit hit = hits.Should().ContainSingle().Subject;
        hit.SearchableText.Should().Be("Is this the real life?\nIs this just fantasy?");
        hit.Candidate.Provider.Should().Be(NeteaseLyricsProvider.PROVIDER_NAME);
        hit.Candidate.ProviderCandidateId.Should().Be("1859245776");
        hit.Candidate.Title.Should().Be("Bohemian Rhapsody");
        hit.Candidate.Artist.Should().Be("Queen");
        hit.Candidate.Album.Should().Be("A Night at the Opera");
        hit.Candidate.Duration.Should().Be(TimeSpan.FromMilliseconds(354000));
        hit.Candidate.Preview.Should().Equal("Is this the real life?", "Is this just fantasy?");
    }

    [Fact]
    public async Task SearchByFragmentAsync_ArrayExcerpt_StripsHighlightTagsAndBlankLines()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(ARRAY_SHAPE_JSON) };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(Query("is this the real life"));

        LyricsFragmentHit hit = hits.Should().ContainSingle().Subject;
        hit.SearchableText.Should().Be("Is this the real life?\nIs this just fantasy?");
        hit.Candidate.Artist.Should().Be("A, B");
    }

    [Fact]
    public async Task SearchByFragmentAsync_SongWithoutExcerpt_IsKeptWithEmptyText()
    {
        const string json = """
            { "result": { "songs": [ { "id": 7, "name": "Untitled", "duration": 1000, "lyrics": null } ] },
              "code": 200 }
            """;
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(json) };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(Query("some words here"));

        LyricsFragmentHit hit = hits.Should().ContainSingle().Subject;
        hit.SearchableText.Should().BeEmpty();
        hit.Candidate.Preview.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByFragmentAsync_NoSongs_ReturnsEmptyList()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "result": { "songCount": 0 }, "code": 200 }""")
        };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(Query("no such words"));

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByFragmentAsync_PayloadCodeNotSuccess_ThrowsPayloadRejected()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json("""{ "code": -460, "message": "Cheating" }""")
        };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        Func<Task> search = () => provider.SearchByFragmentAsync(Query("is this the real life"));

        ProviderPayloadRejectedException ex =
            (await search.Should().ThrowAsync<ProviderPayloadRejectedException>()).Which;
        ex.ProviderName.Should().Be(NeteaseLyricsProvider.PROVIDER_NAME);
        ex.PayloadCode.Should().Be(-460);
    }

    [Fact]
    public async Task SearchByFragmentAsync_HttpFailure_Throws()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        Func<Task> search = () => provider.SearchByFragmentAsync(Query("is this the real life"));

        await search.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SearchByFragmentAsync_LongFragment_IsCappedAtOneHundredCharacters()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(OBJECT_SHAPE_JSON) };
        NeteaseLyricsProvider provider = CreateProvider(handler);
        string fragment = new('a', 150);

        await provider.SearchByFragmentAsync(Query(fragment));

        NameValueCollection parameters = HttpUtility.ParseQueryString(handler.Requests[0].RequestUri!.Query);
        parameters["s"].Should().Be(new string('a', 100));
    }

    [Fact]
    public async Task SearchByFragmentAsync_BlankFragment_SendsNoRequest()
    {
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => Json(OBJECT_SHAPE_JSON) };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        IReadOnlyList<LyricsFragmentHit> hits = await provider.SearchByFragmentAsync(Query("   "));

        hits.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByFragmentAsync_OversizedResponse_Throws()
    {
        string padding = new('x', ProviderResponseReader.MAX_LOOKUP_RESPONSE_BYTES);
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => Json($$"""{ "code": 200, "padding": "{{padding}}" }""")
        };
        NeteaseLyricsProvider provider = CreateProvider(handler);

        Func<Task> search = () => provider.SearchByFragmentAsync(Query("is this the real life"));

        await search.Should().ThrowAsync<HttpRequestException>();
    }
}
