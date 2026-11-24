using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Cantus.Server.Hubs;

public sealed class PlaybackHub : Hub<IPlaybackClient>
{
    private const string CREDENTIAL_KIND_ITEM = "cantus.credential_kind";
    private const string DISPLAY_ID_ITEM = "cantus.display_id";

    private readonly IPlaybackSessionRegistry _registry;
    private readonly ILyricsCacheRepository _lyricsCache;
    private readonly ISpotifyAuthService _authService;
    private readonly ISpotifyPlayerClient _playerClient;
    private readonly ICredentialResolver _credentialResolver;
    private readonly IDisplayPairingService _pairings;
    private readonly IDisplayConnectionTracker _displayConnections;
    private readonly ILocalPlaybackIngest _localIngest;
    private readonly ILogger<PlaybackHub> _logger;

    public PlaybackHub(
        IPlaybackSessionRegistry registry,
        ILyricsCacheRepository lyricsCache,
        ISpotifyAuthService authService,
        ISpotifyPlayerClient playerClient,
        ICredentialResolver credentialResolver,
        IDisplayPairingService pairings,
        IDisplayConnectionTracker displayConnections,
        ILocalPlaybackIngest localIngest,
        ILogger<PlaybackHub> logger)
    {
        _registry = registry;
        _lyricsCache = lyricsCache;
        _authService = authService;
        _playerClient = playerClient;
        _credentialResolver = credentialResolver;
        _pairings = pairings;
        _displayConnections = displayConnections;
        _localIngest = localIngest;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        CallerCredential? caller = null;

        try
        {
            caller = await _credentialResolver.ResolveAsync(Context.GetHttpContext());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve credential for connection {ConnectionId}", Context.ConnectionId);
        }

        string? userId = caller?.Owner.Id;

        // A Display counts as a viewer of its owner, so adaptive polling keeps running
        // for an unattended screen.
        _registry.RegisterConnection(Context.ConnectionId, userId);

        if (caller is not null && !string.IsNullOrEmpty(userId))
        {
            Context.Items[CREDENTIAL_KIND_ITEM] = caller.Kind;
            if (caller.Display is not null)
            {
                Context.Items[DISPLAY_ID_ITEM] = caller.Display.Id;
                if (!_displayConnections.Add(caller.Display.Id, Context))
                {
                    _logger.LogWarning(
                        "Closing connection {ConnectionId} of just-revoked Display {DisplayId}",
                        Context.ConnectionId,
                        caller.Display.Id);
                    Context.Abort();
                    return;
                }
            }

            string group = caller.IsSession ? PlaybackGroups.Owner(userId) : PlaybackGroups.Displays(userId);
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }

        _logger.LogInformation(
            "Client connected to PlaybackHub: {ConnectionId} (Session: {SessionRef}, Credential: {CredentialKind}, Total: {Count})",
            Context.ConnectionId,
            caller is null ? "Unauthenticated" : SessionLogRef.For(userId),
            caller?.Kind.ToString() ?? "None",
            _registry.ConnectedClientsCount);

        try
        {
            if (caller is not null && !string.IsNullOrEmpty(userId))
            {
                UserSession userSession = caller.Owner;
                bool forDisplay = !caller.IsSession;
                UserPlaybackSnapshot? snapshot = _registry.GetUserState(userId);
                bool isPlaying = snapshot?.PlaybackState?.IsPlaying ?? false;
                List<AuthorizedSessionDto> sessionDtos = new() { userSession.ToDto(isPlaying) };

                await SendSnapshotToCallerAsync(snapshot, forDisplay);

                // The session list carries the owner's Session id, which a Display must never see.
                if (!forDisplay)
                {
                    await Clients.Caller.ReceiveSessions(sessionDtos);
                }
                else
                {
                    // Connecting updates the Display's last-seen time in the owner's list.
                    await Clients.Group(PlaybackGroups.Owner(userId)).ReceiveDisplaysChanged();
                }

                DiagnosticsDto diagnostics = new()
                {
                    ConnectedClients = _registry.ConnectedClientsCount,
                    AuthorizedSessions = sessionDtos.Count,
                    PollerStatus = DescribeStatus(userSession, snapshot),
                    ActiveUserId = userSession.Id,
                    ActiveUserName = userSession.DisplayName,
                    ServerTimeUtc = DateTimeOffset.UtcNow
                };

                await Clients.Caller.ReceiveDiagnostics(forDisplay ? diagnostics.ForDisplay() : diagnostics);
            }
            else
            {
                // Unauthenticated client
                await Clients.Caller.ReceiveSessions(new List<AuthorizedSessionDto>());
                await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
                {
                    ConnectedClients = _registry.ConnectedClientsCount,
                    AuthorizedSessions = 0,
                    PollerStatus = "Idle",
                    ActiveUserId = null,
                    ActiveUserName = null,
                    ServerTimeUtc = DateTimeOffset.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error sending initial state to newly connected client {ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        _registry.UnregisterConnection(Context.ConnectionId);
        _pairings.Cancel(Context.ConnectionId);
        _localIngest.ReleaseConnection(Context.ConnectionId);

        if (Context.Items.TryGetValue(DISPLAY_ID_ITEM, out object? item) && item is string displayId)
        {
            _displayConnections.Remove(displayId, Context.ConnectionId);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            string group = GetCredentialKind() is CredentialKind.Display
                ? PlaybackGroups.Displays(userId)
                : PlaybackGroups.Owner(userId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }

        _logger.LogInformation(
            "Client disconnected from PlaybackHub: {ConnectionId} (Total: {Count})",
            Context.ConnectionId,
            _registry.ConnectedClientsCount);

        await base.OnDisconnectedAsync(exception);
    }

    public Task<ClockSyncResponse> SyncClock(long clientSendTimeMs)
    {
        long serverReceiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long serverSendTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return Task.FromResult(new ClockSyncResponse(
            clientSendTimeMs,
            serverReceiveTime,
            serverSendTime));
    }

    /// <summary>
    /// Joins the sign-in handoff group for a client id. A Display connection cannot: the
    /// handoff hands its listener the owner's Session, and a Display holds view rights only.
    /// </summary>
    public async Task RegisterClientLogin(string clientId)
    {
        if (RejectDisplayCaller(nameof(RegisterClientLogin)))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"client_{clientId}");
        }
    }

    /// <summary>
    /// Subscribes a Session connection to its own playback. A Display connection is bound to
    /// its owner when it connects, so this is a no-op for Displays.
    /// </summary>
    public async Task SubscribeToUser(string? userId)
    {
        CallerCredential? caller = await _credentialResolver.ResolveAsync(Context.GetHttpContext());
        if (caller is null || !caller.IsSession)
        {
            return;
        }

        UserSession userSession = caller.Owner;
        if (userId is not null && userSession.Id != userId)
        {
            return;
        }

        Context.Items[CREDENTIAL_KIND_ITEM] = CredentialKind.Session;

        string? prevUserId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (!string.IsNullOrEmpty(prevUserId) && prevUserId != userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, PlaybackGroups.Owner(prevUserId));
        }

        _registry.SetConnectionSubscription(Context.ConnectionId, userId);

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PlaybackGroups.Owner(userId));

            UserPlaybackSnapshot? snapshot = _registry.GetUserState(userId);
            await SendSnapshotToCallerAsync(snapshot, forDisplay: false);

            await Clients.Caller.ReceiveDiagnostics(new DiagnosticsDto
            {
                ConnectedClients = _registry.ConnectedClientsCount,
                AuthorizedSessions = 1,
                PollerStatus = DescribeStatus(userSession, snapshot),
                ActiveUserId = userId,
                ActiveUserName = userSession.DisplayName,
                ServerTimeUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public async Task SetTrackOffset(string trackId, int offsetMs)
    {
        if (RejectDisplayCaller(nameof(SetTrackOffset)) || string.IsNullOrWhiteSpace(trackId))
        {
            return;
        }

        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        _logger.LogInformation("Setting track offset for {TrackId}: {OffsetMs}ms", trackId, offsetMs);

        await _lyricsCache.SetTrackOffsetAsync(trackId, offsetMs);

        UserPlaybackSnapshot? userSnapshot = _registry.GetUserState(userId);
        if (userSnapshot?.PlaybackState?.CurrentTrack?.Id == trackId)
        {
            _registry.UpdateUserState(
                userSnapshot.UserId,
                userSnapshot.DisplayName,
                userSnapshot.PlaybackState,
                userSnapshot.Lyrics,
                offsetMs);
        }

        await Clients.Viewers(userId).ReceiveTrackOffset(new TrackOffsetDto
        {
            TrackId = trackId,
            OffsetMs = offsetMs
        });
    }

    /// <summary>
    /// Publishes what a Local Session's desktop client is playing. Only validates, rate-limits
    /// and queues: a lyrics fetch must never hold this connection's invocation slot, which
    /// SyncClock shares. Returns a <see cref="LocalPublishResult"/> as an int.
    /// </summary>
    public async Task<int> PublishLocalPlayback(LocalPlaybackUpdateDto update)
    {
        if (GetCredentialKind() is not CredentialKind.Session)
        {
            return (int)LocalPublishResult.NotLocalSession;
        }

        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (string.IsNullOrEmpty(userId))
        {
            return (int)LocalPublishResult.NotLocalSession;
        }

        UserSession? session = await _authService.GetSessionByIdAsync(userId);
        if (session is not { Kind: SessionKind.Local })
        {
            return (int)LocalPublishResult.NotLocalSession;
        }

        return (int)_localIngest.Submit(Context.ConnectionId, session, update);
    }

    public Task ReportClientVisibility(bool isVisible)
    {
        _registry.SetConnectionVisibility(Context.ConnectionId, isVisible);
        if (isVisible)
        {
            RequestCallerActivity();
        }

        return Task.CompletedTask;
    }

    public Task RefreshPlayback()
    {
        RequestCallerActivity();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes a playback transport command ("pause", "resume", "next",
    /// "previous") against the caller's own Spotify session. The result tells
    /// the client why a command was rejected (missing
    /// user-modify-playback-state scope on sessions linked before the scope was
    /// added, non-Premium account, or no active device) so it can surface a
    /// hint instead of failing silently. On success polls are requested so
    /// every connected client sees the new state quickly. The result crosses
    /// the wire as an int: the trimmed WASM client cannot deserialize enum
    /// return values, so both ends cast PlayerCommandResult explicitly.
    /// </summary>
    public Task<int> SendPlayerCommand(string command)
    {
        return ExecutePlayerActionAsync(command, (token) => command switch
        {
            "pause" => _playerClient.PausePlaybackAsync(token),
            "resume" => _playerClient.ResumePlaybackAsync(token),
            "next" => _playerClient.SkipToNextAsync(token),
            "previous" => _playerClient.SkipToPreviousAsync(token),
            _ => Task.FromResult(PlayerCommandResult.Failed)
        });
    }

    public Task<int> SetPlayerVolume(int volumePercent)
    {
        return ExecutePlayerActionAsync(
            "volume",
            token => _playerClient.SetVolumeAsync(token, volumePercent));
    }

    public Task<int> SeekPlayback(long positionMs)
    {
        return ExecutePlayerActionAsync(
            "seek",
            token => _playerClient.SeekPlaybackAsync(token, positionMs));
    }

    public Task<int> SetShuffle(bool enabled)
    {
        return ExecutePlayerActionAsync(
            "shuffle",
            token => _playerClient.SetShuffleAsync(token, enabled));
    }

    public Task<int> SetRepeat(string repeatMode)
    {
        return ExecutePlayerActionAsync(
            "repeat",
            token => _playerClient.SetRepeatAsync(token, repeatMode));
    }

    private async Task<int> ExecutePlayerActionAsync(
        string commandName,
        Func<string, Task<PlayerCommandResult>> action)
    {
        if (RejectDisplayCaller(commandName))
        {
            return (int)PlayerCommandResult.Failed;
        }

        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning(
                "Player command {Command} rejected: connection {ConnectionId} has no user subscription",
                commandName,
                Context.ConnectionId);
            return (int)PlayerCommandResult.Failed;
        }

        // A Local Session has no Spotify account; its client drives the OS player directly.
        UserSession? session = await _authService.GetSessionForUserAsync(userId);
        if (session is null || session.Kind == SessionKind.Local || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            _logger.LogWarning(
                "Player command {Command} rejected: no session with an access token (Session: {SessionRef}, SessionFound: {SessionFound})",
                commandName,
                SessionLogRef.For(userId),
                session is not null);
            return (int)PlayerCommandResult.Failed;
        }

        _logger.LogInformation(
            "Player command {Command} requested by Spotify user {SpotifyUserId} (Session: {SessionRef})",
            commandName,
            session.SpotifyUserId,
            SessionLogRef.For(userId));

        PlayerCommandResult result = await action(session.AccessToken);

        if (result == PlayerCommandResult.Success)
        {
            _registry.RequestUserActivity(userId);
            PlaybackFollowUpPolls.Schedule(_registry, userId, _logger);
        }

        return (int)result;
    }

    /// <summary>
    /// Asks for an immediate poll of the caller's owner. A Display's requests are throttled per
    /// owner, so a view-only screen cannot drive the owner's Spotify quota.
    /// </summary>
    private void RequestCallerActivity()
    {
        string? userId = _registry.GetConnectionSubscription(Context.ConnectionId);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        if (GetCredentialKind() is CredentialKind.Display)
        {
            _registry.RequestDisplayActivity(userId);
        }
        else
        {
            _registry.RequestUserActivity(userId);
        }
    }

    private string DescribeStatus(UserSession session, UserPlaybackSnapshot? snapshot)
    {
        PlaybackState? state = snapshot?.PlaybackState;
        if (session.Kind == SessionKind.Local)
        {
            if (_localIngest.IsPublisherOffline(session.Id))
            {
                return PlaybackUpdateApplier.STATUS_PUBLISHER_OFFLINE;
            }

            if (state?.CurrentTrack is null)
            {
                return PlaybackUpdateApplier.STATUS_LOCAL_IDLE;
            }

            return state.IsPlaying
                ? PlaybackUpdateApplier.STATUS_LOCAL_PLAYING
                : PlaybackUpdateApplier.STATUS_LOCAL_PAUSED;
        }

        return state?.IsPlaying == true ? "Active (Playing)" : "Idle";
    }

    private CredentialKind? GetCredentialKind()
    {
        return Context.Items.TryGetValue(CREDENTIAL_KIND_ITEM, out object? kind) && kind is CredentialKind credentialKind
            ? credentialKind
            : null;
    }

    /// <summary>
    /// Methods that change anything, or could hand out owner rights, are closed to Display
    /// connections, which are view-only. They also require a subscription, which only a
    /// Session or Display connect sets, so anonymous callers fail further on.
    /// </summary>
    private bool RejectDisplayCaller(string action)
    {
        if (GetCredentialKind() is not CredentialKind.Display)
        {
            return false;
        }

        _logger.LogWarning(
            "Rejected {Action} from Display connection {ConnectionId}",
            action,
            Context.ConnectionId);
        return true;
    }

    private async Task SendSnapshotToCallerAsync(UserPlaybackSnapshot? snapshot, bool forDisplay)
    {
        if (snapshot?.PlaybackState is not null)
        {
            PlaybackStateDto state = snapshot.PlaybackState.ToDto(snapshot.UserId, snapshot.DisplayName);
            await Clients.Caller.ReceivePlaybackState(forDisplay ? state.ForDisplay() : state);
        }

        if (snapshot?.Lyrics is not null)
        {
            await Clients.Caller.ReceiveLyrics(snapshot.Lyrics.ToDto());
        }

        if (snapshot?.PlaybackState?.CurrentTrack is not null)
        {
            await Clients.Caller.ReceiveTrackOffset(new TrackOffsetDto
            {
                TrackId = snapshot.PlaybackState.CurrentTrack.Id,
                OffsetMs = snapshot.TrackOffsetMs
            });
        }
    }
}
