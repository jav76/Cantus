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
}
