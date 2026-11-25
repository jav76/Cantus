using System.Net;
using System.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.CoverArt;
using FluentAssertions;

namespace Cantus.Infrastructure.Tests.CoverArt;

public class MusicBrainzClientTests
{
    private const string RELEASE_GROUP_ID = "9162580e-5df4-32de-80cc-f45a8d8a9b1d";

    private const string SEARCH_RESPONSE = """
        {
          "count": 2,
          "recordings": [
            {
              "id": "2f5bd758-d052-4585-a090-e42255ce633c",
              "score": 100,
              "title": "Come Together",
              "length": 260200,
              "artist-credit": [
                { "name": "Beatles", "artist": { "id": "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d", "name": "The Beatles" } }
              ],
              "releases": [
                {
                  "id": "31765b9f-e969-4257-855f-c7ea1f657b2a",
                  "title": "Abbey Road",
                  "status": "Official",
                  "date": "1969-09-26",
                  "release-group": {
                    "id": "9162580e-5df4-32de-80cc-f45a8d8a9b1d",
                    "primary-type": "Album"
                  }
                },
                {
                  "id": "732f5793-208c-4aac-86a4-6eca5107884b",
                  "title": "After Dark",
                  "status": "Official",
                  "release-group": {
                    "id": "4ffe95bb-11a9-4970-8ba0-915bad589a2a",
                    "primary-type": "Album",
                    "secondary-types": ["Compilation"]
                  }
                },
                { "id": "x", "title": "No Group" },
                { "id": "y", "title": "Bad Group", "release-group": { "id": "../../evil" } }
              ]
            },
            { "id": "61f5ba46-11f0-4e2b-aa3b-61f817c8127f", "title": "Come Together" }
          ]
        }
        """;

    private static readonly TrackInfo Track = new()
    {
        Id = "local:0123456789abcdef0123456789abcdef",
        Title = "Come Together",
        Artist = "The Beatles",
        Album = "Abbey Road",
        Duration = TimeSpan.FromSeconds(260)
    };

    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task SearchRecordingsAsync_MapsRecordingsArtistsAndReleaseGroups()
    {
        _handler.ResponseHandler = _ => Json(SEARCH_RESPONSE);

        IReadOnlyList<MusicBrainzRecording> recordings = await CreateClient().SearchRecordingsAsync(Track);

        recordings.Should().HaveCount(2);
        MusicBrainzRecording first = recordings[0];
        first.Title.Should().Be("Come Together");
        first.Length.Should().Be(TimeSpan.FromMilliseconds(260_200));
        first.Artists.Should().Equal("Beatles", "The Beatles");
        first.Releases.Should().HaveCount(2, "a release without a valid release group id cannot name a cover");
        first.Releases[0].Should().BeEquivalentTo(new MusicBrainzRelease(
            Guid.Parse(RELEASE_GROUP_ID),
            "Abbey Road",
            "Official",
            "Album",
            [],
            "1969-09-26"));
        first.Releases[1].SecondaryTypes.Should().Equal("Compilation");
        first.Releases[1].Date.Should().BeNull();

        recordings[1].Length.Should().BeNull();
        recordings[1].Artists.Should().BeEmpty();
        recordings[1].Releases.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchRecordingsAsync_AsksForJsonAndTheLargestPage()
    {
        _handler.ResponseHandler = _ => Json("""{ "recordings": [] }""");

        IReadOnlyList<MusicBrainzRecording> recordings = await CreateClient().SearchRecordingsAsync(Track);

        recordings.Should().BeEmpty();
        HttpRequestMessage request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsolutePath.Should().Be("/ws/2/recording");
        request.RequestUri.Query.Should().Contain("limit=100").And.Contain("fmt=json");
        Uri.UnescapeDataString(request.RequestUri.Query).Should().Contain(MusicBrainzClient.BuildQuery(Track));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task SearchRecordingsAsync_WhenMusicBrainzRefuses_Throws(HttpStatusCode statusCode)
    {
        _handler.ResponseHandler = _ => new HttpResponseMessage(statusCode);

        Func<Task> act = () => CreateClient().SearchRecordingsAsync(Track);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void BuildQuery_RequiresTitleArtistAndLengthAndBoostsTheAlbum()
    {
        string query = MusicBrainzClient.BuildQuery(Track);

        query.Should().Be(
            "recording:(come together) AND artistname:(the beatles) AND dur:[255000 TO 265000] release:(abbey road)");
    }

    [Fact]
    public void BuildQuery_WithoutAnAlbum_LeavesTheReleaseClauseOut()
    {
        string query = MusicBrainzClient.BuildQuery(Track with { Album = null });

        query.Should().Be("recording:(come together) AND artistname:(the beatles) AND dur:[255000 TO 265000]");
    }

    [Fact]
    public void BuildQuery_EscapesLuceneSyntaxAndLowerCasesOperators()
    {
        TrackInfo track = Track with
        {
            Title = "Love AND \"Hate\" (Live) [2/2]: What?",
            Artist = "AC/DC + Friends!",
            Album = null
        };

        string query = MusicBrainzClient.BuildQuery(track);

        query.Should().StartWith(
            """recording:(love and \"hate\" \(live\) \[2\/2\]\: what\?) AND artistname:(ac\/dc \+ friends\!) AND dur:""");
    }

    private MusicBrainzClient CreateClient() =>
        new(new HttpClient(_handler) { BaseAddress = new Uri("https://musicbrainz.test") });

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
