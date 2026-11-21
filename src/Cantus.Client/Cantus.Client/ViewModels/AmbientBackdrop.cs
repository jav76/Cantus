using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.ViewModels;

/// <summary>
/// Picks the Dynamic theme backdrop: the sharp artwork, or a blurred copy
/// when background blur is on. The sharp image shows at once and the blurred
/// one replaces it when ready, only if it still matches the current artwork
/// and blur is still on, so a slow blur for an old track never wins. Failed
/// blurs leave the sharp image in place. Generic over the image type so the
/// selection rules are testable without XAML bitmaps.
/// </summary>
internal sealed class AmbientBackdrop<TSource>
    where TSource : class
{
    private readonly Func<string, TSource?> _createSharp;
    private readonly Func<string, CancellationToken, Task<TSource?>> _createBlurred;
    private readonly ILogger _logger;

    private string? _artworkUrl;
    private TSource? _sharp;
    private TSource? _blurred;
    private bool _isBlurEnabled;
    private CancellationTokenSource? _blurCts;

    /// <param name="createSharp">Creates the full artwork image; may return null.</param>
    /// <param name="createBlurred">Creates the blurred image; null or an exception keeps the sharp one.</param>
    public AmbientBackdrop(
        Func<string, TSource?> createSharp,
        Func<string, CancellationToken, Task<TSource?>> createBlurred,
        ILogger logger)
    {
        _createSharp = createSharp;
        _createBlurred = createBlurred;
        _logger = logger;
    }

    /// <summary>Raised on the calling context whenever <see cref="Source"/> changes.</summary>
    public event Action? SourceChanged;

    public TSource? Source { get; private set; }

    public bool IsBlurred { get; private set; }

    /// <summary>The blur in flight, if any; exposed so tests can await it.</summary>
    internal Task? PendingBlur { get; private set; }

    public void Update(string? artworkUrl, bool isBlurEnabled)
    {
        _isBlurEnabled = isBlurEnabled;

        if (artworkUrl != _artworkUrl)
        {
            CancelPendingBlur();
            _artworkUrl = artworkUrl;
            _blurred = null;
            _sharp = artworkUrl is null ? null : TryCreateSharp(artworkUrl);
        }

        if (artworkUrl is null)
        {
            Show(null, isBlurred: false);
            return;
        }

        if (!isBlurEnabled)
        {
            CancelPendingBlur();
            Show(_sharp, isBlurred: false);
            return;
        }

        if (_blurred is not null)
        {
            Show(_blurred, isBlurred: true);
            return;
        }

        Show(_sharp, isBlurred: false);
        if (_blurCts is null)
        {
            CancellationTokenSource blurCts = new();
            _blurCts = blurCts;
            PendingBlur = BlurAsync(artworkUrl, blurCts);
        }
    }

    private async Task BlurAsync(string artworkUrl, CancellationTokenSource blurCts)
    {
        try
        {
            TSource? blurred = await _createBlurred(artworkUrl, blurCts.Token);
            if (!ReferenceEquals(_blurCts, blurCts) || artworkUrl != _artworkUrl)
            {
                return;
            }

            if (blurred is null)
            {
                _logger.LogDebug("No blurred backdrop for {AlbumArtUrl}; keeping the sharp artwork", artworkUrl);
                return;
            }

            _blurred = blurred;
            if (_isBlurEnabled)
            {
                Show(blurred, isBlurred: true);
            }
        }
        catch (OperationCanceledException) when (blurCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blurring the backdrop for {AlbumArtUrl} failed; keeping the sharp artwork", artworkUrl);
        }
        finally
        {
            if (ReferenceEquals(_blurCts, blurCts))
            {
                _blurCts = null;
                PendingBlur = null;
            }

            blurCts.Dispose();
        }
    }

    private TSource? TryCreateSharp(string artworkUrl)
    {
        try
        {
            return _createSharp(artworkUrl);
        }
        catch (Exception ex)
        {
            // Headless test environments cannot create XAML bitmaps.
            _logger.LogDebug(ex, "Could not create the backdrop image for {AlbumArtUrl}", artworkUrl);
            return null;
        }
    }

    private void CancelPendingBlur()
    {
        CancellationTokenSource? blurCts = _blurCts;
        _blurCts = null;
        PendingBlur = null;
        blurCts?.Cancel();
    }

    private void Show(TSource? source, bool isBlurred)
    {
        if (ReferenceEquals(Source, source) && IsBlurred == isBlurred)
        {
            return;
        }

        Source = source;
        IsBlurred = isBlurred;
        SourceChanged?.Invoke();
    }
}
