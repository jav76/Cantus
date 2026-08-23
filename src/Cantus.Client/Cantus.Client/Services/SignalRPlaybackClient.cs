using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cantus.Client.Services;

public sealed record PlaybackStatePayload(
    TrackInfo? CurrentTrack,
    long ProgressMs,
    bool IsPlaying,
    DateTimeOffset TimestampUtc,
    string? DeviceName,
    int? VolumePercent,
    string? ActiveUserId,
    string? ActiveUserDisplayName);

public sealed record LyricLinePayload(long TimestampMs, string Text);

public sealed record LyricsPayload(
    string TrackId,
    string Title,
    string Artist,
    string? Album,
    bool IsSynced,
    bool IsInstrumental,
    IReadOnlyList<LyricLinePayload> Lines,
    string? PlainLyrics);

public sealed record TrackOffsetPayload(string TrackId, int OffsetMs);

public sealed record DiagnosticsPayload(
    int ConnectedClients,
    int AuthorizedSessions,
    string PollerStatus,
    int ActivePollIntervalMs,
    string? ActiveUserId,
    string? ActiveUserName,
    DateTimeOffset ServerTimeUtc);

public sealed record AuthorizedSessionPayload(
    string Id,
    string SpotifyUserId,
    string DisplayName,
    string? Email,
    string? ProfileImageUrl,
    bool IsCurrentlyPlaying,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ClockSyncResponsePayload(
    long ClientSendTimeMs,
    long ServerReceiveTimeMs,
    long ServerSendTimeMs);

public sealed record NtpSample(long RttMs, long OffsetMs, long TimestampMs);

public sealed class SignalRPlaybackClient : IAsyncDisposable
{
    private HubConnection? _connection;
    private Timer? _clockSyncTimer;
    private readonly string _serverUrl;

    private readonly List<NtpSample> _ntpHistory = new();
    private readonly object _ntpLock = new();
    private const int MaxNtpSamples = 5;

    public long RttMs { get; private set; }
    public long ClockOffsetMs { get; private set; }
    public string TransportType { get; private set; } = "Unknown";
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public event Action<string>? ConnectionStateChanged;
    public event Action<PlaybackStatePayload>? PlaybackStateReceived;
    public event Action<LyricsPayload>? LyricsReceived;
    public event Action<TrackOffsetPayload>? TrackOffsetReceived;
    public event Action<IReadOnlyList<AuthorizedSessionPayload>>? SessionsReceived;
    public event Action<DiagnosticsPayload>? DiagnosticsReceived;

    public SignalRPlaybackClient(string? serverUrl = null)
    {
#if __WASM__
        _serverUrl = string.IsNullOrEmpty(serverUrl) ? "/hubs/playback" : serverUrl;
#else
        _serverUrl = string.IsNullOrEmpty(serverUrl) ? "http://localhost:5000/hubs/playback" : serverUrl;
#endif
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(_serverUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _connection.On<PlaybackStatePayload>("ReceivePlaybackState", state => PlaybackStateReceived?.Invoke(state));
        _connection.On<LyricsPayload>("ReceiveLyrics", lyrics => LyricsReceived?.Invoke(lyrics));
        _connection.On<TrackOffsetPayload>("ReceiveTrackOffset", offset => TrackOffsetReceived?.Invoke(offset));
        _connection.On<IReadOnlyList<AuthorizedSessionPayload>>("ReceiveSessions", sessions => SessionsReceived?.Invoke(sessions));
        _connection.On<DiagnosticsPayload>("ReceiveDiagnostics", diag => DiagnosticsReceived?.Invoke(diag));

        _connection.Reconnecting += ex =>
        {
            ConnectionStateChanged?.Invoke("Reconnecting");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            ConnectionStateChanged?.Invoke("Connected");
            TransportType = "WebSockets";
            _ = SyncClockAsync();
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            ConnectionStateChanged?.Invoke("Disconnected");
            return Task.CompletedTask;
        };

        try
        {
            ConnectionStateChanged?.Invoke("Connecting");
            await _connection.StartAsync(cancellationToken);
            ConnectionStateChanged?.Invoke("Connected");
            TransportType = "WebSockets";

            _clockSyncTimer = new Timer(async _ => await SyncClockAsync(), null, 0, 5000);
        }
        catch (Exception)
        {
            ConnectionStateChanged?.Invoke("Disconnected");
        }
    }

    public async Task SyncClockAsync()
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var response = await _connection.InvokeAsync<ClockSyncResponsePayload>("SyncClock", t1);
            long t4 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            long t2 = response.ServerReceiveTimeMs;
            long t3 = response.ServerSendTimeMs;

            long rawRtt = Math.Max(0, (t4 - t1) - (t3 - t2));
            long rawOffset = ((t2 - t1) + (t3 - t4)) / 2;

            ProcessNtpSample(rawRtt, rawOffset, t4);
        }
        catch
        {
            // Transient clock sync error ignored
        }
    }

    public void ProcessNtpSample(long rawRtt, long rawOffset, long timestampMs)
    {
        lock (_ntpLock)
        {
            _ntpHistory.Add(new NtpSample(rawRtt, rawOffset, timestampMs));
            if (_ntpHistory.Count > MaxNtpSamples)
            {
                _ntpHistory.RemoveAt(0);
            }

            var samples = _ntpHistory.ToList();
            if (samples.Count == 1)
            {
                RttMs = samples[0].RttMs;
                ClockOffsetMs = samples[0].OffsetMs;
                return;
            }

            // If we have >= 3 samples, discard the single highest RTT spike
            var filtered = samples;
            if (samples.Count >= 3)
            {
                long maxRtt = samples.Max(s => s.RttMs);
                var worstSample = samples.First(s => s.RttMs == maxRtt);
                filtered = samples.Where(s => !ReferenceEquals(s, worstSample)).ToList();
            }

            // Weighted average by inverse RTT (lower latency = higher trust)
            double totalWeight = 0;
            double weightedOffsetSum = 0;
            double totalRttSum = 0;

            foreach (var s in filtered)
            {
                double weight = 1.0 / Math.Max(1, s.RttMs);
                totalWeight += weight;
                weightedOffsetSum += s.OffsetMs * weight;
                totalRttSum += s.RttMs;
            }

            long computedOffset = (long)Math.Round(weightedOffsetSum / totalWeight);
            long computedRtt = (long)Math.Round(totalRttSum / filtered.Count);

            // Apply Exponential Moving Average (EMA: alpha = 0.35)
            const double alpha = 0.35;
            RttMs = (long)(alpha * computedRtt + (1 - alpha) * RttMs);
            ClockOffsetMs = (long)(alpha * computedOffset + (1 - alpha) * ClockOffsetMs);
        }
    }

    public async Task SetTrackOffsetAsync(string trackId, int offsetMs)
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("SetTrackOffset", trackId, offsetMs);
        }
    }

    public async Task SubscribeToUserAsync(string? userId)
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("SubscribeToUser", userId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _clockSyncTimer?.Dispose();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
