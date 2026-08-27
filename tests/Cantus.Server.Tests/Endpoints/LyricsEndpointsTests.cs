using System.Net;
using System.Net.Http.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Server.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Endpoints;

public sealed class LyricsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<ILyricsCacheRepository> _mockCacheRepo = new();

    public LyricsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => _mockCacheRepo.Object);
            });
        });
    }

    [Fact]
    public async Task GetCachedLyrics_WhenFound_ReturnsLyricsDto()
    {
        HttpClient client = _factory.CreateClient();

        SyncedLyrics lyrics = new()
        {
            TrackId = "track-123",
            Title = "Song Title",
            Artist = "Artist Name",
            Lines = [new LyricLine(TimeSpan.FromSeconds(10), "Line 1")]
        };

        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("track-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(lyrics);

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/track-123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        LyricsDto? dto = await response.Content.ReadFromJsonAsync<LyricsDto>();
        dto.Should().NotBeNull();
        dto!.TrackId.Should().Be("track-123");
        dto.Lines.Should().HaveCount(1);
        dto.Lines[0].Text.Should().Be("Line 1");
    }

    [Fact]
    public async Task GetCachedLyrics_WhenNotFound_Returns404()
    {
        HttpClient client = _factory.CreateClient();

        _mockCacheRepo
            .Setup(c => c.GetCachedLyricsAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncedLyrics?)null);

        HttpResponseMessage response = await client.GetAsync("/api/lyrics/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetTrackOffset_SavesOffsetAndReturnsOk()
    {
        HttpClient client = _factory.CreateClient();

        TrackOffsetDto request = new()
        {
            TrackId = "track-123",
            OffsetMs = 500
        };

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/lyrics/offset", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _mockCacheRepo.Verify(c => c.SetTrackOffsetAsync("track-123", 500, It.IsAny<CancellationToken>()), Times.Once);
    }
}

