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
    public Microsoft.UI.Xaml.Visibility PlayingVisibility => IsCurrentlyPlaying ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
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

public sealed class SignalRPlaybackClient : IAsyncDisposable
{
    private HubConnection? _connection;
    private Timer? _clockSyncTimer;
    private readonly string? _configuredServerUrl;

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

    public string? SessionToken { get; set; }

    public SignalRPlaybackClient(string? serverUrl = null, string? sessionToken = null)
    {
        _configuredServerUrl = serverUrl;
        SessionToken = sessionToken;
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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
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
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
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

    public async Task LogoutAsync()
    {
        try
        {
            string url = ResolveServerUrl();
            string baseUrl = url.Contains("/hubs/playback")
                ? url.Substring(0, url.IndexOf("/hubs/playback", StringComparison.Ordinal))
                : url;

            using var http = new System.Net.Http.HttpClient();
            await http.PostAsync($"{baseUrl}/api/auth/logout", null);
        }
        catch
        {
        }

        SessionToken = null;
        if (_connection is not null)
        {
            await _connection.StopAsync();
            await _connection.StartAsync();
        }
    }

    public async Task ReconnectWithTokenAsync(string? sessionToken)
    {
        SessionToken = sessionToken;
        if (_connection is not null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
        await StartAsync();
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
