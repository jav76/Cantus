using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cantus.Client.Services;

[TraceLog]
public interface ISignalRPlaybackClient : IAsyncDisposable
{
    TimeSpan ReconnectInterval { get; set; }
    long RttMs { get; }
    long ClockOffsetMs { get; }
    string TransportType { get; }
    HubConnectionState State { get; }
    string? SessionToken { get; set; }
    string ServerBaseUrl { get; }

    event Action<string>? ConnectionStateChanged;
    event Action<PlaybackStatePayload>? PlaybackStateReceived;
    event Action<LyricsPayload>? LyricsReceived;
    event Action<TrackOffsetPayload>? TrackOffsetReceived;
    event Action<IReadOnlyList<AuthorizedSessionPayload>>? SessionsReceived;
    event Action<DiagnosticsPayload>? DiagnosticsReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);
    Task SyncClockAsync();
    Task SetTrackOffsetAsync(string trackId, int offsetMs);
    Task SubscribeToUserAsync(string? userId);
    Task LogoutAsync();
    Task ReconnectWithTokenAsync(string? sessionToken);
}
