namespace Cantus.Infrastructure.CoverArt;

/// <summary>
/// Spaces requests to a service that allows one per second from an address. Callers wait
/// their turn instead of being refused, which suits background work with no one waiting.
/// </summary>
public sealed class RequestPacer : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumInterval;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;

    public RequestPacer(TimeProvider timeProvider, TimeSpan minimumInterval)
    {
        _timeProvider = timeProvider;
        _minimumInterval = minimumInterval;
    }

    /// <summary>Completes when the caller may send, at least the minimum interval after the previous caller.</summary>
    public async Task WaitForTurnAsync(CancellationToken cancellationToken = default)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            TimeSpan wait = _nextAllowedUtc - _timeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _timeProvider, cancellationToken);
            }

            _nextAllowedUtc = _timeProvider.GetUtcNow() + _minimumInterval;
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Dispose() => _turn.Dispose();
}
