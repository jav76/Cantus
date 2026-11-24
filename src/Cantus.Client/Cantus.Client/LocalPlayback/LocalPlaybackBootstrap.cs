using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Wiring for local player publishing. Starts the platform source at launch, creates a Local Session
/// only when the owner picks this computer's players, and runs the publisher only while the server
/// says the current Session is a Local one.
/// </summary>
public sealed class LocalPlaybackBootstrap : ILocalPlayback, IAsyncDisposable
{
    /// <summary>Optional: prefills the join code field.</summary>
    public const string JOIN_CODE_VARIABLE = "CANTUS_LOCAL_JOIN_CODE";
    public const int MAX_DISPLAY_NAME_LENGTH = 64;
    public const string FALLBACK_DISPLAY_NAME = "Cantus desktop";

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<LocalPlaybackBootstrap>();

    private readonly ISignalRPlaybackClient _client;
    private readonly ILocalPlayerSource _source;
    private readonly ILocalSessionApi _sessionApi;
    private readonly LocalPlaybackPublisher _publisher;
    private readonly LocalPlayerPreferences _preferences;
    private readonly string _displayName;
    private readonly object _gate = new();

    private bool _started;
    private bool _sourceReady;
    private bool _isLocalSession;
    private bool _disposed;

    public LocalPlaybackBootstrap(
        ISignalRPlaybackClient client,
        ILocalPlayerSource source,
        ILocalSessionApi sessionApi,
        LocalPlaybackPublisher publisher,
        LocalPlayerPreferences preferences,
        string displayName,
        string? joinCodePrefill)
    {
        _client = client;
        _source = source;
        _sessionApi = sessionApi;
        _publisher = publisher;
        _preferences = preferences;
        _displayName = NormalizeDisplayName(displayName);
        JoinCodePrefill = string.IsNullOrWhiteSpace(joinCodePrefill) ? null : joinCodePrefill.Trim();

        // Subscribed now, before the first connection, so the Session list sent on connect is seen.
        _client.SessionsReceived += OnSessionsReceived;
        _client.AuthSessionReceived += OnAuthSessionReceived;
        _client.SessionRevoked += OnSessionRevoked;
    }

    public static LocalPlaybackBootstrap Create(ISignalRPlaybackClient client, ISettingsStore settings)
    {
        ILocalPlayerSource source = LocalPlayerSources.Create();
        LocalPlayerPreferences preferences = new(settings);
        LocalPlaybackPublisher publisher = new(
            source,
            new SignalRLocalPlaybackSink(client),
            new PlayerSelectionPolicy(),
            preferences,
            TimeProvider.System);

        return new LocalPlaybackBootstrap(
            client,
            source,
            new LocalSessionApi(client),
            publisher,
            preferences,
            source.IsSupported ? ReadMachineName() : FALLBACK_DISPLAY_NAME,
            Environment.GetEnvironmentVariable(JOIN_CODE_VARIABLE));
    }

    // Created on every head now, including WASM, where the machine name may be unavailable.
    private static string ReadMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            return FALLBACK_DISPLAY_NAME;
        }
    }

    public static string NormalizeDisplayName(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return FALLBACK_DISPLAY_NAME;
        }

        return trimmed.Length > MAX_DISPLAY_NAME_LENGTH ? trimmed[..MAX_DISPLAY_NAME_LENGTH] : trimmed;
    }

    public event Action? AvailabilityChanged;

    public event Action? PlayersChanged;

    public event Action? FollowedPlayerChanged
    {
        add => _publisher.FollowedPlayerChanged += value;
        remove => _publisher.FollowedPlayerChanged -= value;
    }

    public bool IsSupported
    {
        get
        {
            lock (_gate)
            {
                return _sourceReady;
            }
        }
    }

    public bool IsPublishing => _publisher.IsRunning;

    public string? JoinCodePrefill { get; }

    public IReadOnlyList<LocalPlayerSnapshot> Players => IsSupported ? _source.Players : [];

    public LocalPlayerSnapshot? FollowedPlayer => _publisher.FollowedPlayer;

    /// <summary>Starts the source only. A Local Session is created when the owner asks for one.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        if (!_source.IsSupported)
        {
            _logger.LogInformation("Local players are not readable on this platform");
            return;
        }

        try
        {
            await _source.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local player source failed to start");
            return;
        }

        if (!_source.IsSupported)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _sourceReady = true;
        }

        _source.PlayersChanged += OnSourcePlayersChanged;
        UpdatePublisher();
        AvailabilityChanged?.Invoke();
    }

    public async Task<LocalSessionResult> CreateSessionAsync(
        string? joinCode,
        Func<Task>? beforeConnect = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return new LocalSessionResult(LocalSessionStatus.Failed);
        }

        if (!string.IsNullOrWhiteSpace(_client.DisplayToken) && string.IsNullOrWhiteSpace(_client.SessionToken))
        {
            _logger.LogInformation("This client is a paired Display, so no Local Session is created");
            return new LocalSessionResult(LocalSessionStatus.Failed);
        }

        LocalSessionResult result;
        try
        {
            result = await _sessionApi.CreateAsync(_displayName, joinCode, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not create a Local Session");
            return new LocalSessionResult(LocalSessionStatus.Failed);
        }

        if (result.Status != LocalSessionStatus.Created || result.Session is null)
        {
            _logger.LogWarning("Could not create a Local Session: {Status}", result.Status);
            return result.Status == LocalSessionStatus.Created
                ? new LocalSessionResult(LocalSessionStatus.Failed)
                : result;
        }

        _logger.LogInformation("Created a Local Session for this computer");
        if (beforeConnect is not null)
        {
            await beforeConnect();
        }

        await _client.ReconnectWithTokenAsync(result.Session.Id);
        SetLocalSession(true);
        return result;
    }

    public void NotifySignedOut() => SetLocalSession(false);

    public bool IsPlayerEnabled(string appId) => _preferences.IsEnabled(appId);

    public void SetPlayerEnabled(string appId, bool enabled)
    {
        _preferences.SetEnabled(appId, enabled);
        _publisher.Refresh();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _client.SessionsReceived -= OnSessionsReceived;
        _client.AuthSessionReceived -= OnAuthSessionReceived;
        _client.SessionRevoked -= OnSessionRevoked;
        _source.PlayersChanged -= OnSourcePlayersChanged;
        _publisher.Dispose();
        await _source.DisposeAsync();
    }

    private void OnSourcePlayersChanged() => PlayersChanged?.Invoke();

    private void OnSessionsReceived(IReadOnlyList<AuthorizedSessionPayload>? sessions)
    {
        AuthorizedSessionPayload? current = sessions?.FirstOrDefault(s => s is not null && s.Id == _client.SessionToken)
            ?? sessions?.FirstOrDefault(s => s is not null);
        SetLocalSession(current?.IsLocal == true);
    }

    private void OnAuthSessionReceived(AuthorizedSessionPayload? session) => SetLocalSession(session?.IsLocal == true);

    private void OnSessionRevoked(string _) => SetLocalSession(false);

    private void SetLocalSession(bool isLocal)
    {
        lock (_gate)
        {
            _isLocalSession = isLocal;
        }

        UpdatePublisher();
    }

    private void UpdatePublisher()
    {
        lock (_gate)
        {
            if (_sourceReady && _isLocalSession && !_disposed)
            {
                _publisher.Start();
            }
            else
            {
                _publisher.Stop();
            }
        }
    }
}
