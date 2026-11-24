using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// Opt-in wiring for local player publishing (<c>CANTUS_LOCAL_PLAYER=1</c>, desktop only). Starts the
/// platform source, creates a Local Session when the client holds neither a Session nor a Display token,
/// and runs the publisher only while the server says the authorized Session is a Local one.
/// </summary>
public sealed class LocalPlaybackBootstrap : IAsyncDisposable
{
    public const string ENABLE_VARIABLE = "CANTUS_LOCAL_PLAYER";
    public const string JOIN_CODE_VARIABLE = "CANTUS_LOCAL_JOIN_CODE";
    public const int MAX_DISPLAY_NAME_LENGTH = 64;
    public const string FALLBACK_DISPLAY_NAME = "Cantus desktop";

    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<LocalPlaybackBootstrap>();

    private readonly ISignalRPlaybackClient _client;
    private readonly ILocalPlayerSource _source;
    private readonly ILocalSessionApi _sessionApi;
    private readonly LocalPlaybackPublisher _publisher;
    private readonly string _displayName;
    private readonly string? _joinCode;
    private readonly object _gate = new();

    private bool _started;
    private bool _sourceReady;
    private bool _isLocalSession;
    private bool _creatingSession;
    private bool _retryCreationOnConnect;
    private bool _disposed;

    public LocalPlaybackBootstrap(
        ISignalRPlaybackClient client,
        ILocalPlayerSource source,
        ILocalSessionApi sessionApi,
        LocalPlaybackPublisher publisher,
        string displayName,
        string? joinCode)
    {
        _client = client;
        _source = source;
        _sessionApi = sessionApi;
        _publisher = publisher;
        _displayName = NormalizeDisplayName(displayName);
        _joinCode = string.IsNullOrWhiteSpace(joinCode) ? null : joinCode.Trim();

        // Subscribed now, before the first connection, so the Session list sent on connect is seen.
        _client.SessionsReceived += OnSessionsReceived;
        _client.AuthSessionReceived += OnAuthSessionReceived;
        _client.SessionRevoked += OnSessionRevoked;
        _client.Connected += OnConnected;
    }

    public static bool IsEnabled(Func<string, string?> readVariable) =>
        string.Equals(readVariable(ENABLE_VARIABLE)?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>Null unless <c>CANTUS_LOCAL_PLAYER=1</c>, so the app is unchanged without the flag.</summary>
    public static LocalPlaybackBootstrap? CreateFromEnvironment(ISignalRPlaybackClient client, ISettingsStore settings)
    {
        if (!IsEnabled(Environment.GetEnvironmentVariable))
        {
            return null;
        }

        ILocalPlayerSource source = LocalPlayerSources.Create();
        LocalPlaybackPublisher publisher = new(
            source,
            new SignalRLocalPlaybackSink(client),
            new PlayerSelectionPolicy(),
            new LocalPlayerPreferences(settings),
            TimeProvider.System);

        return new LocalPlaybackBootstrap(
            client,
            source,
            new LocalSessionApi(client),
            publisher,
            Environment.MachineName,
            Environment.GetEnvironmentVariable(JOIN_CODE_VARIABLE));
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

    public bool IsPublishing => _publisher.IsRunning;

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
            _sourceReady = true;
        }

        UpdatePublisher();

        if (!string.IsNullOrWhiteSpace(_client.DisplayToken) && string.IsNullOrWhiteSpace(_client.SessionToken))
        {
            _logger.LogInformation("This client is a paired Display, so no Local Session is created");
            return;
        }

        await TryCreateLocalSessionAsync(cancellationToken);
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
        _client.Connected -= OnConnected;
        _publisher.Dispose();
        await _source.DisposeAsync();
    }

    private bool NeedsLocalSession =>
        string.IsNullOrWhiteSpace(_client.SessionToken) && string.IsNullOrWhiteSpace(_client.DisplayToken);

    private void OnConnected()
    {
        lock (_gate)
        {
            if (!_retryCreationOnConnect)
            {
                return;
            }
        }

        _ = TryCreateLocalSessionAsync(CancellationToken.None);
    }

    private async Task TryCreateLocalSessionAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed || !NeedsLocalSession)
            {
                _retryCreationOnConnect = false;
                return;
            }

            if (_creatingSession)
            {
                return;
            }

            _creatingSession = true;
            _retryCreationOnConnect = false;
        }

        bool retry = false;
        try
        {
            retry = await CreateLocalSessionAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not create a Local Session");
            retry = true;
        }
        finally
        {
            lock (_gate)
            {
                _creatingSession = false;
                _retryCreationOnConnect = retry;
            }
        }
    }

    /// <summary>Returns true when the failure may pass, so creation is tried again on the next connect.</summary>
    private async Task<bool> CreateLocalSessionAsync(CancellationToken cancellationToken)
    {
        LocalSessionResult result = await _sessionApi.CreateAsync(_displayName, _joinCode, cancellationToken);
        switch (result.Status)
        {
            case LocalSessionStatus.Created when result.Session is not null:
                lock (_gate)
                {
                    // A sign-in or pairing can complete while the request is in flight; it wins.
                    if (_disposed || !NeedsLocalSession)
                    {
                        _logger.LogInformation(
                            "A Session or Display arrived while the Local Session was being created; keeping it");
                        return false;
                    }
                }

                _logger.LogInformation("Created a Local Session for this computer");
                await _client.ReconnectWithTokenAsync(result.Session.Id);
                return false;

            case LocalSessionStatus.Disabled:
                _logger.LogWarning("The server does not allow Local Sessions");
                return false;

            case LocalSessionStatus.JoinCodeRequired:
            case LocalSessionStatus.JoinCodeInvalid:
                _logger.LogWarning(
                    "The server needs a valid join code for a Local Session ({Status}); set {Variable}",
                    result.Status,
                    JOIN_CODE_VARIABLE);
                return false;

            case LocalSessionStatus.LimitReached:
                _logger.LogWarning("The server holds its maximum number of Local Sessions");
                return false;

            case LocalSessionStatus.Failed:
            case LocalSessionStatus.RateLimited:
                _logger.LogWarning("Could not create a Local Session ({Status}); retrying on the next connect", result.Status);
                return true;

            default:
                _logger.LogWarning("Could not create a Local Session: {Status}", result.Status);
                return false;
        }
    }

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
            if (_sourceReady && _isLocalSession)
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
