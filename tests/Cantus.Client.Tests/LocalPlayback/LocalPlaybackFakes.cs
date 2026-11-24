using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.LocalPlayback;
using Cantus.Client.Services;

namespace Cantus.Client.Tests.LocalPlayback;

internal static class LocalPlayers
{
    public static readonly LocalPlayerCapabilities ALL_CAPABILITIES = new(true, true, true, true, true);

    public static LocalTrack Song(string title = "Ballin", string artist = "parkbreezy", int seconds = 184) =>
        new(title, artist, "Reflections", TimeSpan.FromSeconds(seconds), "https://i.scdn.co/image/abc", null);

    public static LocalPlayerSnapshot Player(
        string instanceId,
        LocalPlayerStatus status,
        LocalTrack? track,
        TimeSpan? startedAt = null,
        string? appId = null,
        TimeSpan position = default,
        TimeSpan capturedAt = default,
        LocalPlayerCapabilities? capabilities = null,
        int seekGeneration = 0) =>
        new(
            instanceId,
            appId ?? instanceId,
            appId ?? instanceId,
            status,
            track,
            position,
            capturedAt,
            1.0,
            capabilities ?? ALL_CAPABILITIES,
            startedAt,
            seekGeneration);
}

internal sealed class FakeLocalPlayerSource : ILocalPlayerSource
{
    private IReadOnlyList<LocalPlayerSnapshot> _players = [];

    public bool IsSupported { get; set; } = true;

    public bool SupportedAfterStart { get; set; } = true;

    public int StartCount { get; private set; }

    public IReadOnlyList<LocalPlayerSnapshot> Players => _players;

    public event Action? PlayersChanged;

    public void SetPlayers(params LocalPlayerSnapshot[] players)
    {
        _players = players;
        PlayersChanged?.Invoke();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        IsSupported = SupportedAfterStart;
        return Task.CompletedTask;
    }

    public Task<bool> SendAsync(
        string instanceId,
        LocalPlayerCommand command,
        long? positionMs = null,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeLocalPlaybackSink : ILocalPlaybackSink
{
    private readonly Queue<int> _results = new();

    public bool IsConnected { get; set; } = true;

    public string? ConnectionId { get; set; } = "connection-1";

    public long? ServerClockOffsetMs { get; set; } = 250;

    public List<LocalPlaybackUpdatePayload> Published { get; } = new();

    /// <summary>When set, the next publish waits on this until the test completes it.</summary>
    public TaskCompletionSource<int>? Pending { get; set; }

    public event Action? Connected;

    public void EnqueueResults(params LocalPublishResult[] results)
    {
        foreach (LocalPublishResult result in results)
        {
            _results.Enqueue((int)result);
        }
    }

    public void RaiseConnected() => Connected?.Invoke();

    public Task<int> PublishAsync(LocalPlaybackUpdatePayload payload)
    {
        Published.Add(payload);
        if (Pending is { } pending)
        {
            Pending = null;
            return pending.Task;
        }

        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : (int)LocalPublishResult.Accepted);
    }
}
