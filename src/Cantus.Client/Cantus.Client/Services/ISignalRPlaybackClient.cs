using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Logging;
using Cantus.Core.Models;
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
    string ClientId { get; }
    string? SessionToken { get; set; }
    string? DisplayToken { get; set; }
    string? ConnectionId { get; }
    string ServerBaseUrl { get; }

    event Action<string>? ConnectionStateChanged;
    event Action<PlaybackStatePayload>? PlaybackStateReceived;
    event Action<LyricsPayload>? LyricsReceived;
    event Action<TrackOffsetPayload>? TrackOffsetReceived;
    event Action<IReadOnlyList<AuthorizedSessionPayload>>? SessionsReceived;
    event Action<AuthorizedSessionPayload>? AuthSessionReceived;
    event Action<string>? SessionRevoked;
    event Action<DiagnosticsPayload>? DiagnosticsReceived;
    event Action<DisplayPairedPayload>? DisplayPaired;
    event Action<string>? DisplayRevoked;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);
    Task SyncClockAsync();
    Task SetTrackOffsetAsync(string trackId, int offsetMs);
    Task<PlayerCommandResult> SendPlayerCommandAsync(string command);
    Task<PlayerCommandResult> SetPlayerVolumeAsync(int volumePercent);
    Task<PlayerCommandResult> SeekPlaybackAsync(long positionMs);
    Task<PlayerCommandResult> SetShuffleAsync(bool enabled);
    Task<PlayerCommandResult> SetRepeatAsync(string repeatMode);
    Task SubscribeToUserAsync(string? userId);
    Task ReportVisibilityAsync(bool isVisible);
    Task RefreshPlaybackAsync();
    Task LogoutAsync();
    Task ReconnectWithTokenAsync(string? sessionToken);

    /// <summary>Replaces the Display token (null drops it) and opens a new hub connection with it.</summary>
    Task ReconnectWithDisplayTokenAsync([Redact] string? displayToken);
}
