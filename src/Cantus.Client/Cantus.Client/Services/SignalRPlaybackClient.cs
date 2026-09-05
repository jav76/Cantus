using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Cantus.Client.Services;

public sealed class TrackInfoPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("albumArtUrl")]
    public string? AlbumArtUrl { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("isExplicit")]
    public bool IsExplicit { get; set; }
}

public sealed class PlaybackStatePayload
{
    [JsonPropertyName("currentTrack")]
    public TrackInfoPayload? CurrentTrack { get; set; }

    [JsonPropertyName("progressMs")]
    public long ProgressMs { get; set; }

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("volumePercent")]
    public int? VolumePercent { get; set; }

    [JsonPropertyName("activeUserId")]
    public string? ActiveUserId { get; set; }

    [JsonPropertyName("activeUserDisplayName")]
    public string? ActiveUserDisplayName { get; set; }
}

public sealed class LyricLinePayload
{
    [JsonPropertyName("timestampMs")]
    public long TimestampMs { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public sealed class LyricsPayload
{
    [JsonPropertyName("trackId")]
    public string TrackId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("isSynced")]
    public bool IsSynced { get; set; }

    [JsonPropertyName("isInstrumental")]
    public bool IsInstrumental { get; set; }

    [JsonPropertyName("lines")]
    public List<LyricLinePayload> Lines { get; set; } = new();

    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }
}

public sealed class TrackOffsetPayload
{
    [JsonPropertyName("trackId")]
    public string TrackId { get; set; } = string.Empty;

    [JsonPropertyName("offsetMs")]
    public int OffsetMs { get; set; }
}

public sealed class DiagnosticsPayload
{
    [JsonPropertyName("connectedClients")]
    public int ConnectedClients { get; set; }

    [JsonPropertyName("authorizedSessions")]
    public int AuthorizedSessions { get; set; }

    [JsonPropertyName("pollerStatus")]
    public string PollerStatus { get; set; } = "Idle";

    [JsonPropertyName("activePollIntervalMs")]
    public int ActivePollIntervalMs { get; set; }

    [JsonPropertyName("activeUserId")]
    public string? ActiveUserId { get; set; }

    [JsonPropertyName("activeUserName")]
    public string? ActiveUserName { get; set; }

    [JsonPropertyName("serverTimeUtc")]
    public DateTimeOffset ServerTimeUtc { get; set; }
}

public sealed class AuthorizedSessionPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("spotifyUserId")]
    public string SpotifyUserId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("profileImageUrl")]
    public string? ProfileImageUrl { get; set; }

    [JsonPropertyName("isCurrentlyPlaying")]
    public bool IsCurrentlyPlaying { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; }

    [JsonIgnore]
    public Microsoft.UI.Xaml.Visibility PlayingVisibility =>
        IsCurrentlyPlaying ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

public sealed class ClockSyncResponsePayload
{
    [JsonPropertyName("clientSendTimeMs")]
    public long ClientSendTimeMs { get; set; }

    [JsonPropertyName("serverReceiveTimeMs")]
    public long ServerReceiveTimeMs { get; set; }

    [JsonPropertyName("serverSendTimeMs")]
    public long ServerSendTimeMs { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TrackInfoPayload))]
[JsonSerializable(typeof(PlaybackStatePayload))]
[JsonSerializable(typeof(LyricLinePayload))]
[JsonSerializable(typeof(LyricsPayload))]
[JsonSerializable(typeof(TrackOffsetPayload))]
[JsonSerializable(typeof(DiagnosticsPayload))]
[JsonSerializable(typeof(AuthorizedSessionPayload))]
[JsonSerializable(typeof(List<AuthorizedSessionPayload>))]
[JsonSerializable(typeof(ClockSyncResponsePayload))]
public partial class CantusJsonContext : JsonSerializerContext
{
}

public sealed record NtpSample(long RttMs, long OffsetMs, long TimestampMs);

public sealed class SignalRPlaybackClient : ISignalRPlaybackClient
{
    private HubConnection? _connection;
    private Timer? _clockSyncTimer;
    private Timer? _reconnectPollerTimer;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly string? _configuredServerUrl;
    private bool _isDisposed;

    private readonly List<NtpSample> _ntpHistory = new();
    private readonly object _ntpLock = new();
    private const int MAX_NTP_SAMPLES = 5;

    public string ClientId { get; } = Guid.NewGuid().ToString("N");
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(5);
    public long RttMs { get; private set; }
    public long ClockOffsetMs { get; private set; }
    public string TransportType { get; private set; } = "Unknown";
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public event Action<string>? ConnectionStateChanged;
    public event Action<PlaybackStatePayload>? PlaybackStateReceived;
    public event Action<LyricsPayload>? LyricsReceived;
    public event Action<TrackOffsetPayload>? TrackOffsetReceived;
    public event Action<IReadOnlyList<AuthorizedSessionPayload>>? SessionsReceived;
    public event Action<AuthorizedSessionPayload>? AuthSessionReceived;
    public event Action<string>? SessionRevoked;
    public event Action<DiagnosticsPayload>? DiagnosticsReceived;

    internal void RaiseLyricsReceived(LyricsPayload payload)
    {
        LyricsReceived?.Invoke(payload);
    }

    internal void RaisePlaybackStateReceived(PlaybackStatePayload payload)
    {
        PlaybackStateReceived?.Invoke(payload);
    }

    internal void RaiseSessionsReceived(IReadOnlyList<AuthorizedSessionPayload> sessions)
    {
        SessionsReceived?.Invoke(sessions);
    }

    internal void RaiseAuthSessionReceived(AuthorizedSessionPayload payload)
    {
        AuthSessionReceived?.Invoke(payload);
    }

    internal void RaiseSessionRevoked(string userId)
    {
        SessionRevoked?.Invoke(userId);
    }

    public string? SessionToken { get; set; }

    public SignalRPlaybackClient(
        string? serverUrl = null,
        string? sessionToken = null,
        TimeSpan? reconnectInterval = null)
    {
        _configuredServerUrl = serverUrl;
        SessionToken = sessionToken ?? LoadPersistedSessionToken();
        if (reconnectInterval.HasValue)
        {
            ReconnectInterval = reconnectInterval.Value;
        }
    }

    private static string GetSessionFilePath()
    {
#if __WASM__
        return string.Empty;
#else
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string cantusDir = System.IO.Path.Combine(appData, "Cantus");
        System.IO.Directory.CreateDirectory(cantusDir);
        return System.IO.Path.Combine(cantusDir, "session.txt");
#endif
    }

    private static string? LoadPersistedSessionToken()
    {
#if __WASM__
        return null;
#else
        try
        {
            string path = GetSessionFilePath();
            if (System.IO.File.Exists(path))
            {
                string token = System.IO.File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
        }
        catch
        {
        }
        return null;
#endif
    }

    private static void SavePersistedSessionToken(string token)
    {
#if !__WASM__
        try
        {
            string path = GetSessionFilePath();
            System.IO.File.WriteAllText(path, token.Trim());
        }
        catch
        {
        }
#endif
    }

    private static void ClearPersistedSessionToken()
    {
#if !__WASM__
        try
        {
            string path = GetSessionFilePath();
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch
        {
        }
#endif
    }

    public string ServerBaseUrl
    {
        get
        {
            string url = ResolveServerUrl();
            if (url.Contains("/hubs/playback", StringComparison.Ordinal))
            {
                return url.Substring(0, url.IndexOf("/hubs/playback", StringComparison.Ordinal));
            }
            return url;
        }
    }

    private string ResolveServerUrl()
    {
        if (!string.IsNullOrWhiteSpace(_configuredServerUrl))
        {
            return _configuredServerUrl;
        }

#if __WASM__
        string origin = WasmInterop.GetCurrentOrigin();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            return $"{origin}/hubs/playback";
        }
#endif
        return "http://localhost:5000/hubs/playback";
    }

    private void EnsureConnectionBuilt()
    {
        if (_connection is not null)
        {
            return;
        }

        string effectiveUrl = ResolveServerUrl();

#if __WASM__
        WasmInterop.CleanAuthQuery();
#endif

        _connection = new HubConnectionBuilder()
            .WithUrl(effectiveUrl, options =>
            {
                if (!string.IsNullOrWhiteSpace(SessionToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(SessionToken);
                }
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.TypeInfoResolver = CantusJsonContext.Default;
            })
            .WithAutomaticReconnect(
            [
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            ])
            .Build();

        _connection.On<PlaybackStatePayload>("ReceivePlaybackState", state =>
        {
            PlaybackStateReceived?.Invoke(state);
        });

        _connection.On<LyricsPayload>("ReceiveLyrics", lyrics =>
        {
            LyricsReceived?.Invoke(lyrics);
        });

        _connection.On<TrackOffsetPayload>("ReceiveTrackOffset", offset =>
        {
            TrackOffsetReceived?.Invoke(offset);
        });

        _connection.On<List<AuthorizedSessionPayload>>("ReceiveSessions", sessions =>
        {
            SessionsReceived?.Invoke(sessions);
        });

        _connection.On<AuthorizedSessionPayload>("ReceiveAuthSession", session =>
        {
            if (session is not null && !string.IsNullOrWhiteSpace(session.Id))
            {
                SavePersistedSessionToken(session.Id);
                SessionToken = session.Id;
                AuthSessionReceived?.Invoke(session);
                _ = ReconnectWithTokenAsync(session.Id);
            }
        });

        _connection.On<string>("ReceiveSessionRevoked", userId =>
        {
            ClearPersistedSessionToken();
            SessionToken = null;
            SessionRevoked?.Invoke(userId);
        });

        _connection.On<DiagnosticsPayload>("ReceiveDiagnostics", diag =>
        {
            DiagnosticsReceived?.Invoke(diag);
        });

        _connection.Reconnecting += ex =>
        {
            ConnectionStateChanged?.Invoke("Reconnecting");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            ConnectionStateChanged?.Invoke("Connected");
            TransportType = "WebSockets";
            if (string.IsNullOrWhiteSpace(SessionToken))
            {
                _ = _connection.InvokeAsync("RegisterClientLogin", ClientId);
            }
            _ = SyncClockAsync();
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            ConnectionStateChanged?.Invoke("Disconnected");
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureReconnectPollerStarted();
        await TryConnectAsync(cancellationToken);
    }

    private void EnsureReconnectPollerStarted()
    {
        _reconnectPollerTimer ??= new Timer(
            async _ => await OnReconnectPollerTickAsync(),
            null,
            ReconnectInterval,
            ReconnectInterval);
    }

    private async Task OnReconnectPollerTickAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (State == HubConnectionState.Disconnected)
        {
            await TryConnectAsync();
        }
    }

    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return false;
        }

        if (!await _connectionLock.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            if (_connection is not null && _connection.State == HubConnectionState.Connected)
            {
                return true;
            }

            EnsureConnectionBuilt();

            if (_connection is null || _connection.State != HubConnectionState.Disconnected)
            {
                return _connection?.State == HubConnectionState.Connected;
            }

            ConnectionStateChanged?.Invoke("Connecting");
            await _connection.StartAsync(cancellationToken);
            ConnectionStateChanged?.Invoke("Connected");
            TransportType = "WebSockets";

            if (string.IsNullOrWhiteSpace(SessionToken))
            {
                _ = _connection.InvokeAsync("RegisterClientLogin", ClientId, cancellationToken);
            }

            _clockSyncTimer ??= new Timer(async _ => await SyncClockAsync(), null, 0, 5000);
            _ = SyncClockAsync();
            return true;
        }
        catch (Exception)
        {
            ConnectionStateChanged?.Invoke("Disconnected");
            return false;
        }
        finally
        {
            _connectionLock.Release();
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
            ClockSyncResponsePayload response = await _connection
                .InvokeAsync<ClockSyncResponsePayload>("SyncClock", t1);
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
            if (_ntpHistory.Count > MAX_NTP_SAMPLES)
            {
                _ntpHistory.RemoveAt(0);
            }

            List<NtpSample> samples = _ntpHistory.ToList();
            if (samples.Count == 1)
            {
                RttMs = samples[0].RttMs;
                ClockOffsetMs = samples[0].OffsetMs;
                return;
            }

            // If we have >= 3 samples, discard the single highest RTT spike
            List<NtpSample> filtered = samples;
            if (samples.Count >= 3)
            {
                long maxRtt = samples.Max(s => s.RttMs);
                NtpSample worstSample = samples.First(s => s.RttMs == maxRtt);
                filtered = samples.Where(s => !ReferenceEquals(s, worstSample)).ToList();
            }

            // Weighted average by inverse RTT (lower latency = higher trust)
            double totalWeight = 0;
            double weightedOffsetSum = 0;
            double totalRttSum = 0;

            foreach (NtpSample s in filtered)
            {
                double weight = 1.0 / Math.Max(1, s.RttMs);
                totalWeight += weight;
                weightedOffsetSum += s.OffsetMs * weight;
                totalRttSum += s.RttMs * weight;
            }

            long computedRtt = (long)(totalRttSum / totalWeight);
            long computedOffset = (long)(weightedOffsetSum / totalWeight);

            // Apply Exponential Moving Average (EMA: alpha = 0.35)
            const double ALPHA = 0.35;
            RttMs = (long)(ALPHA * computedRtt + (1 - ALPHA) * RttMs);
            ClockOffsetMs = (long)(ALPHA * computedOffset + (1 - ALPHA) * ClockOffsetMs);
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

    public async Task ReportVisibilityAsync(bool isVisible)
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("ReportClientVisibility", isVisible);
            }
            catch
            {
            }
        }
    }

    public async Task RefreshPlaybackAsync()
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("RefreshPlayback");
            }
            catch
            {
            }
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            string url = ResolveServerUrl();
            string baseUrl = url.Contains("/hubs/playback", StringComparison.Ordinal)
                ? url.Substring(0, url.IndexOf("/hubs/playback", StringComparison.Ordinal))
                : url;

            using System.Net.Http.HttpClient http = new();
            if (!string.IsNullOrWhiteSpace(SessionToken))
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);
            }
            await http.PostAsync($"{baseUrl}/api/auth/logout", null);
        }
        catch
        {
        }

        ClearPersistedSessionToken();
        SessionToken = null;
        await _connectionLock.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
        await TryConnectAsync();
    }

    public async Task ReconnectWithTokenAsync(string? sessionToken)
    {
        SessionToken = sessionToken;
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            SavePersistedSessionToken(sessionToken);
        }
        else
        {
            ClearPersistedSessionToken();
        }

        await _connectionLock.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
        await TryConnectAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _reconnectPollerTimer?.Dispose();
        _clockSyncTimer?.Dispose();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _connectionLock.Dispose();
    }
}
