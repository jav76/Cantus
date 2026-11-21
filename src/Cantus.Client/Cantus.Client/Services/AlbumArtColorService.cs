using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Microsoft.Extensions.Logging;
using StbImageSharp;

namespace Cantus.Client.Services;

/// <summary>
/// Downloads album artwork, extracts its dominant color swatches and prepares
/// the blurred backdrop. Each download also keeps a small thumbnail, so the
/// blur for a track whose colors were already extracted needs no second fetch.
/// Successful results are cached by URL; failures are not cached so that
/// transient network errors retry on the next track change.
/// </summary>
public sealed class AlbumArtColorService
{
    private const int MAX_IMAGE_BYTES = 4 * 1024 * 1024;
    private const int FETCH_TIMEOUT_SECONDS = 10;
    private const int CACHE_CAPACITY = 16;
    private const int BLURRED_CACHE_CAPACITY = 8;
    private const int BLUR_LONG_SIDE_PX = 80;
    private const int BLUR_RADIUS_PX = 4;
    private const int BLUR_PASSES = 3;

    private static AlbumArtColorService? _instance;
    public static AlbumArtColorService Instance => _instance ??= new AlbumArtColorService();

    private readonly HttpClient _httpClient;
    private readonly ILogger<AlbumArtColorService> _logger;
    private readonly BoundedUrlCache<IReadOnlyList<ColorSwatch>> _swatchesByUrl = new(CACHE_CAPACITY);
    private readonly BoundedUrlCache<RgbaImage> _thumbnailsByUrl = new(CACHE_CAPACITY);
    private readonly BoundedUrlCache<byte[]> _blurredPngByUrl = new(BLURRED_CACHE_CAPACITY);

    public AlbumArtColorService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS) })
    {
    }

    public AlbumArtColorService(HttpClient httpClient)
        : this(httpClient, ClientLoggingManager.CreateLogger<AlbumArtColorService>())
    {
    }

    public AlbumArtColorService(HttpClient httpClient, ILogger<AlbumArtColorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool TryGetCachedSwatches(string? albumArtUrl, [NotNullWhen(true)] out IReadOnlyList<ColorSwatch>? swatches)
    {
        swatches = null;
        if (string.IsNullOrWhiteSpace(albumArtUrl))
        {
            return false;
        }

        return _swatchesByUrl.TryGet(albumArtUrl, out swatches);
    }

    public async Task<IReadOnlyList<ColorSwatch>?> GetSwatchesAsync(string? albumArtUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(albumArtUrl))
        {
            return null;
        }

        if (TryGetCachedSwatches(albumArtUrl, out IReadOnlyList<ColorSwatch>? cachedSwatches))
        {
            return cachedSwatches;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            RgbaImage? image = await DownloadImageAsync(albumArtUrl, cancellationToken).ConfigureAwait(false);
            if (image is null)
            {
                return null;
            }

            IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(image.Pixels, image.Width, image.Height);
            if (swatches.Count == 0)
            {
                return null;
            }

            _swatchesByUrl.Add(albumArtUrl, swatches);
            return swatches;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to extract colors from {AlbumArtUrl}",
                albumArtUrl);
            return null;
        }
    }

    /// <summary>
    /// A small, heavily blurred PNG of the artwork for the Dynamic theme
    /// backdrop, or null when the artwork cannot be fetched or decoded.
    /// Reuses the thumbnail kept by <see cref="GetSwatchesAsync"/> when present.
    /// </summary>
    public async Task<byte[]?> GetBlurredArtworkPngAsync(string? albumArtUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(albumArtUrl))
        {
            return null;
        }

        if (_blurredPngByUrl.TryGet(albumArtUrl, out byte[]? cachedPng))
        {
            return cachedPng;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!_thumbnailsByUrl.TryGet(albumArtUrl, out RgbaImage? thumbnail))
            {
                thumbnail = await DownloadImageAsync(albumArtUrl, cancellationToken).ConfigureAwait(false);
                if (thumbnail is null)
                {
                    return null;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            RgbaImage blurred = ArtworkBlur.BoxBlur(thumbnail, BLUR_RADIUS_PX, BLUR_PASSES);
            byte[] png = PngEncoder.Encode(blurred);
            _blurredPngByUrl.Add(albumArtUrl, png);
            return png;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to blur album art from {AlbumArtUrl}",
                albumArtUrl);
            return null;
        }
    }

    /// <summary>
    /// Fetches and decodes the artwork, caches its thumbnail for the blur and
    /// returns the full-size pixels, or null for HTTP errors and oversized or
    /// empty payloads. Decoding errors throw to the caller.
    /// </summary>
    private async Task<RgbaImage?> DownloadImageAsync(string albumArtUrl, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS));

        using HttpResponseMessage response = await _httpClient
            .GetAsync(albumArtUrl, timeoutCts.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Album art request returned {StatusCode} for {AlbumArtUrl}",
                (int)response.StatusCode,
                albumArtUrl);
            return null;
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MAX_IMAGE_BYTES)
        {
            _logger.LogWarning(
                "Album art exceeds {MaxBytes} bytes: {AlbumArtUrl}",
                MAX_IMAGE_BYTES,
                albumArtUrl);
            return null;
        }

        byte[] imageBytes = await response.Content
            .ReadAsByteArrayAsync(timeoutCts.Token)
            .ConfigureAwait(false);

        if (imageBytes.Length == 0 || imageBytes.Length > MAX_IMAGE_BYTES)
        {
            return null;
        }

        timeoutCts.Token.ThrowIfCancellationRequested();

        ImageResult decoded = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
        RgbaImage image = new(decoded.Width, decoded.Height, decoded.Data);
        _thumbnailsByUrl.Add(albumArtUrl, ArtworkBlur.Downscale(image, BLUR_LONG_SIDE_PX));
        return image;
    }
}
