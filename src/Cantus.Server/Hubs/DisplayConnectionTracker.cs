using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Hubs;

public sealed class DisplayConnectionTracker : IDisplayConnectionTracker
{
    // Long enough to cover a connection whose token was validated before the revocation
    // and that registers after it; short enough that the set stays small.
    private const int REVOKED_RETENTION_SECONDS = 60;

    private readonly Dictionary<string, Dictionary<string, HubCallerContext>> _connectionsByDisplay =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _revokedAtUtc = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly IHubContext<PlaybackHub, IPlaybackClient> _hubContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DisplayConnectionTracker> _logger;

    public DisplayConnectionTracker(
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        TimeProvider timeProvider,
        ILogger<DisplayConnectionTracker> logger)
    {
        _hubContext = hubContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool Add(string displayId, HubCallerContext connection)
    {
        lock (_lock)
        {
            PurgeRevoked(_timeProvider.GetUtcNow());
            if (_revokedAtUtc.ContainsKey(displayId))
            {
                return false;
            }

            if (!_connectionsByDisplay.TryGetValue(displayId, out Dictionary<string, HubCallerContext>? connections))
            {
                connections = new Dictionary<string, HubCallerContext>(StringComparer.Ordinal);
                _connectionsByDisplay[displayId] = connections;
            }

            connections[connection.ConnectionId] = connection;
            return true;
        }
    }

    public void Remove(string displayId, string connectionId)
    {
        lock (_lock)
        {
            if (_connectionsByDisplay.TryGetValue(displayId, out Dictionary<string, HubCallerContext>? connections) &&
                connections.Remove(connectionId) &&
                connections.Count == 0)
            {
                _connectionsByDisplay.Remove(displayId);
            }
        }
    }

    public IReadOnlyList<string> GetConnectionIds(string displayId)
    {
        lock (_lock)
        {
            return _connectionsByDisplay.TryGetValue(displayId, out Dictionary<string, HubCallerContext>? connections)
                ? connections.Keys.ToList()
                : Array.Empty<string>();
        }
    }

    public async Task<int> DisconnectAsync(string displayId)
    {
        List<HubCallerContext> connections = new();
        lock (_lock)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            PurgeRevoked(now);
            _revokedAtUtc[displayId] = now;

            if (_connectionsByDisplay.Remove(displayId, out Dictionary<string, HubCallerContext>? tracked))
            {
                connections.AddRange(tracked.Values);
            }
        }

        if (connections.Count == 0)
        {
            return 0;
        }

        List<string> connectionIds = connections.Select(c => c.ConnectionId).ToList();
        try
        {
            await _hubContext.Clients.Clients(connectionIds).ReceiveDisplayRevoked(displayId);
        }
        catch (Exception ex)
        {
            // The connections are closed below either way; the notice only lets the client
            // tell a revocation apart from a network drop.
            _logger.LogWarning(ex, "Failed to notify Display {DisplayId} of its revocation", displayId);
        }

        foreach (HubCallerContext connection in connections)
        {
            connection.Abort();
        }

        _logger.LogInformation(
            "Closed {Count} connection(s) of revoked Display {DisplayId}",
            connections.Count,
            displayId);
        return connections.Count;
    }

    private void PurgeRevoked(DateTimeOffset now)
    {
        TimeSpan retention = TimeSpan.FromSeconds(REVOKED_RETENTION_SECONDS);
        List<string> expired = _revokedAtUtc
            .Where(entry => now - entry.Value >= retention)
            .Select(entry => entry.Key)
            .ToList();

        foreach (string displayId in expired)
        {
            _revokedAtUtc.Remove(displayId);
        }
    }
}
