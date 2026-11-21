using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using FluentAssertions;
using StbImageSharp;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class AlbumArtColorServiceTests
{
    private const string TEST_URL = "https://i.scdn.co/image/test-album-art";
    private const int OVERSIZED_IMAGE_BYTES = 5 * 1024 * 1024; // 5MB

    // 8x8 solid red PNG
    private const string RED_PNG_BASE64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
        "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAWSURBVChTY/jPwPAfH2ZAF0DHw0MBAMLXf4F1" +
        "N6FEAAAAAElFTkSuQmCC";

    // 8x8 PNG: top 6 rows blue (48 px), bottom 2 rows yellow (16 px)
    private const string BLUE_YELLOW_PNG_BASE64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
        "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAmSURBVChTY2Bg+P8fP8YQQMcYAugYQwAdYwig" +
        "YwwBNAxGeDBBBQB5PY9xfgNXXQAAAABJRU5ErkJggg==";

    // 16x16 solid red JPEG (lossy encoding, decoded colors are near-red)
    private const string RED_JPEG_BASE64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsK" +
        "CwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQU" +
        "FBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIA" +
        "AhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQA" +
        "AAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3" +
        "ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWm" +
        "p6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEA" +
        "AwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSEx" +
        "BhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElK" +
        "U1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3" +
        "uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD50ooo" +
        "r8MP9Uz/2Q==";

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? AsyncResponseHandler { get; set; }

        public int InvocationCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;

            if (AsyncResponseHandler is not null)
            {
                return await AsyncResponseHandler(request);
            }

            if (ResponseHandler is not null)
            {
                return ResponseHandler(request);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage CreateImageResponse(string base64)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Convert.FromBase64String(base64))
        };
    }

    private static AlbumArtColorService CreateService(MockHttpMessageHandler handler)
    {
        return new AlbumArtColorService(new HttpClient(handler));
    }

    [Fact]
    public async Task GetSwatchesAsync_ValidPng_ReturnsRedDominantSwatches()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? swatches = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        swatches.Should().NotBeNull();
        swatches![0].Color.R.Should().BeGreaterThan(200);
        swatches[0].Color.G.Should().BeLessThan(50);
        swatches[0].Color.B.Should().BeLessThan(50);
    }

    [Fact]
    public async Task GetSwatchesAsync_ValidJpeg_ReturnsRedDominantSwatches()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_JPEG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? swatches = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        swatches.Should().NotBeNull();
        swatches![0].Color.R.Should().BeGreaterThan(200);
        swatches[0].Color.G.Should().BeLessThan(80);
        swatches[0].Color.B.Should().BeLessThan(80);
    }

    [Fact]
    public async Task GetSwatchesAsync_TwoTonePng_RanksSwatchesByPopulation()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(BLUE_YELLOW_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? swatches = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        swatches.Should().NotBeNull();
        swatches!.Count.Should().Be(2);
        swatches[0].Color.B.Should().BeGreaterThan(200); // dominant blue
        swatches[0].Population.Should().Be(48);
        swatches[1].Color.R.Should().BeGreaterThan(200); // secondary yellow
        swatches[1].Color.G.Should().BeGreaterThan(200);
        swatches[1].Population.Should().Be(16);
    }

    [Fact]
    public async Task GetSwatchesAsync_SecondCall_ServesFromCache()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? firstResult = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);
        IReadOnlyList<ColorSwatch>? secondResult = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        handler.InvocationCount.Should().Be(1);
        secondResult.Should().BeSameAs(firstResult);
    }

    [Fact]
    public async Task GetSwatchesAsync_HttpError_ReturnsNull()
    {
        // Arrange
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? swatches = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        swatches.Should().BeNull();
    }

    [Fact]
    public async Task GetSwatchesAsync_NonImagePayload_ReturnsNull()
    {
        // Arrange
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            }
        };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? swatches = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        swatches.Should().BeNull();
    }

    [Fact]
    public async Task GetSwatchesAsync_OversizedContentLength_ReturnsNull()
    {
        // Arrange
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ =>
            {
                HttpResponseMessage response = CreateImageResponse(RED_PNG_BASE64);
                response.Content.Headers.ContentLength = OVERSIZED_IMAGE_BYTES;
                return response;
            }
        };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? swatches = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        swatches.Should().BeNull();
    }

    [Fact]
    public async Task GetSwatchesAsync_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await service.GetSwatchesAsync(TEST_URL, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetSwatchesAsync_FailedRequest_IsNotCached()
    {
        // Arrange
        MockHttpMessageHandler handler = new();
        handler.ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        AlbumArtColorService service = CreateService(handler);

        // Act - first call fails, second call succeeds
        IReadOnlyList<ColorSwatch>? failedResult = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);
        handler.ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64);
        IReadOnlyList<ColorSwatch>? retriedResult = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Assert
        failedResult.Should().BeNull();
        retriedResult.Should().NotBeNull();
        handler.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSwatchesAsync_NullOrEmptyUrl_ReturnsNullWithoutRequest()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        IReadOnlyList<ColorSwatch>? nullResult = await service.GetSwatchesAsync(null, CancellationToken.None);
        IReadOnlyList<ColorSwatch>? emptyResult = await service.GetSwatchesAsync("  ", CancellationToken.None);

        // Assert
        nullResult.Should().BeNull();
        emptyResult.Should().BeNull();
        handler.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task TryGetCachedSwatches_AfterSuccessfulFetch_ReturnsCachedResult()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        service.TryGetCachedSwatches(TEST_URL, out IReadOnlyList<ColorSwatch>? beforeFetch).Should().BeFalse();
        IReadOnlyList<ColorSwatch>? fetched = await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);
        bool cacheHit = service.TryGetCachedSwatches(TEST_URL, out IReadOnlyList<ColorSwatch>? afterFetch);

        // Assert
        beforeFetch.Should().BeNull();
        cacheHit.Should().BeTrue();
        afterFetch.Should().BeSameAs(fetched);
    }

    [Fact]
    public async Task GetBlurredArtworkPngAsync_ReturnsADecodableRedPng()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        byte[]? png = await service.GetBlurredArtworkPngAsync(TEST_URL, CancellationToken.None);

        // Assert
        png.Should().NotBeNull();
        ImageResult decoded = ImageResult.FromMemory(png!, ColorComponents.RedGreenBlueAlpha);
        decoded.Width.Should().Be(8);
        decoded.Height.Should().Be(8);
        decoded.Data[0].Should().BeGreaterThan(200);
        decoded.Data[1].Should().BeLessThan(40);
    }

    [Fact]
    public async Task GetBlurredArtworkPngAsync_AfterSwatchExtraction_DoesNotFetchAgain()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_JPEG_BASE64) };
        AlbumArtColorService service = CreateService(handler);
        await service.GetSwatchesAsync(TEST_URL, CancellationToken.None);

        // Act
        byte[]? first = await service.GetBlurredArtworkPngAsync(TEST_URL, CancellationToken.None);
        byte[]? second = await service.GetBlurredArtworkPngAsync(TEST_URL, CancellationToken.None);

        // Assert
        handler.InvocationCount.Should().Be(1);
        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetBlurredArtworkPngAsync_HttpError_ReturnsNull()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
        AlbumArtColorService service = CreateService(handler);

        // Act
        byte[]? png = await service.GetBlurredArtworkPngAsync(TEST_URL, CancellationToken.None);

        // Assert
        png.Should().BeNull();
    }

    [Fact]
    public async Task GetBlurredArtworkPngAsync_NonImagePayload_ReturnsNull()
    {
        // Arrange
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            },
        };
        AlbumArtColorService service = CreateService(handler);

        // Act
        byte[]? png = await service.GetBlurredArtworkPngAsync(TEST_URL, CancellationToken.None);

        // Assert
        png.Should().BeNull();
    }

    [Fact]
    public async Task GetBlurredArtworkPngAsync_CancelledToken_ThrowsOperationCanceled()
    {
        // Arrange
        MockHttpMessageHandler handler = new() { ResponseHandler = _ => CreateImageResponse(RED_PNG_BASE64) };
        AlbumArtColorService service = CreateService(handler);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act
        Func<Task> act = () => service.GetBlurredArtworkPngAsync(TEST_URL, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
