using System.Threading.Channels;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Infrastructure.CoverArt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Server.Services;

/// <summary>A cover the catalogue found for a track that was broadcast without one.</summary>
public sealed record CoverArtResolved(string TrackId, string ArtUrl);

/// <summary>
/// Looks up covers in the background for tracks that are already on screen without one, so the
/// catalogue never delays a playback or lyrics broadcast.
/// </summary>
public interface ICoverArtBackfill
{
    /// <summary>False when the operator turned catalogue lookups off.</summary>
    bool IsEnabled { get; }

    /// <summary>Raised on the lookup thread when a cover was found. Handlers must not block.</summary>
    event EventHandler<CoverArtResolved>? Resolved;

    /// <summary>
    /// Queues a lookup and returns at once. Call it after the track's playback state is in the
    /// registry: a track no Session is showing when its turn comes is skipped.
    /// </summary>
    void Request(TrackInfo track);
}

/// <summary>
/// One worker for the whole server takes tracks from a small queue, which keeps catalogue
/// traffic serial. When the queue is full the oldest request is dropped: it is the one most
/// likely to have been skipped past, and it is asked again the next time the track plays.
/// </summary>
public sealed class CoverArtBackfill : ICoverArtBackfill, IDisposable
{
    public const int QUEUE_CAPACITY = 32;

    /// <summary>
    /// Waits before asking again when the catalogue could not be reached. MusicBrainz sheds
    /// search load with short-lived 503s, so one failed request says little. Waiting on the
    /// only worker also holds back every other lookup while the catalogue is struggling.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackSessionRegistry _registry;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CoverArtOptions> _options;
    private readonly ILogger<CoverArtBackfill> _logger;
    private readonly object _lock = new();
    private readonly HashSet<string> _queuedTrackIds = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<TrackInfo> _queue;
    private Task? _worker;
    private bool _disposed;

    public CoverArtBackfill(
        IServiceScopeFactory scopeFactory,
        IPlaybackSessionRegistry registry,
        TimeProvider timeProvider,
        IOptions<CoverArtOptions> options,
        ILogger<CoverArtBackfill> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
        _queue = Channel.CreateBounded<TrackInfo>(
            new BoundedChannelOptions(QUEUE_CAPACITY)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            dropped => Forget(dropped.Id));
    }

    public bool IsEnabled => _options.Value.Enabled;

    public event EventHandler<CoverArtResolved>? Resolved;

    public void Request(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!IsEnabled)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed || !_queuedTrackIds.Add(track.Id))
            {
                return;
            }

            _worker ??= Task.Run(RunWorkerAsync);
        }

        _queue.Writer.TryWrite(track);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stopping.Cancel();
        _queue.Writer.TryComplete();
    }

    private async Task RunWorkerAsync()
    {
        CancellationToken stopping = _stopping.Token;
        try
        {
            await foreach (TrackInfo track in _queue.Reader.ReadAllAsync(stopping))
            {
                try
                {
                    await ResolveAsync(track, stopping);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cover art lookup failed for track {TrackId}", track.Id);
                }
                finally
                {
                    Forget(track.Id);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private async Task ResolveAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        for (int attempt = 0; IsShownWithoutArt(track.Id); attempt++)
        {
            CoverArtLookup lookup;
            await using (AsyncServiceScope scope = _scopeFactory.CreateAsyncScope())
            {
                lookup = await scope.ServiceProvider
                    .GetRequiredService<ICoverArtService>()
                    .ResolveAsync(track, cancellationToken);
            }

            if (lookup is { Status: CoverArtLookupStatus.Found, ArtUrl: { } artUrl })
            {
                Resolved?.Invoke(this, new CoverArtResolved(track.Id, artUrl));
                return;
            }

            if (lookup.Status != CoverArtLookupStatus.Unresolved || attempt >= RetryDelays.Count)
            {
                return;
            }

            await Task.Delay(RetryDelays[attempt], _timeProvider, cancellationToken);
        }
    }

    private bool IsShownWithoutArt(string trackId)
    {
        foreach (UserPlaybackSnapshot snapshot in _registry.GetAllSnapshots())
        {
            if (snapshot.PlaybackState?.CurrentTrack is { AlbumArtUrl: null } track
                && string.Equals(track.Id, trackId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void Forget(string trackId)
    {
        lock (_lock)
        {
            _queuedTrackIds.Remove(trackId);
        }
    }
}
