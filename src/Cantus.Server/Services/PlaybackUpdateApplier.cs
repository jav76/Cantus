using System.Collections.Concurrent;
using Cantus.Core.Interfaces;
using Cantus.Core.Logging;
using Cantus.Core.Models;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Services;

/// <summary>One playback state to apply to a Session's snapshot and broadcast to its viewers.</summary>
/// <param name="AllowLyricsFetch">False when the Session is over its lyric budget: a track change
/// then broadcasts empty lyrics instead of calling a provider.</param>
/// <param name="ClaimedSpotifyTrackId">A validated Spotify track id a local player reported. Carried
/// for the track identity guard; the track key is still the derived local key.</param>
public sealed record PlaybackUpdate(
    string SessionId,
    string DisplayName,
    PlaybackState State,
    bool AllowLyricsFetch = true,
    string? ClaimedSpotifyTrackId = null);

/// <summary>
/// Applies a playback state to a Session: detects a track change, fetches lyrics and the saved
/// offset for a new track, writes the registry and broadcasts state, lyrics and offset to the
/// owner and their Displays. Owns the last applied track per Session.
/// </summary>
public interface IPlaybackUpdateApplier
{
    Task ApplyAsync(
        PlaybackUpdate update,
        ILyricsProvider lyricsProvider,
        ILyricsCacheRepository lyricsCache,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows a Session's playback paused at its projected position, keeping track and lyrics,
    /// because the connection publishing it went away.
    /// </summary>
    Task MarkPublisherOfflineAsync(string sessionId, string displayName, CancellationToken cancellationToken = default);

    void Forget(string sessionId);
}

public sealed class PlaybackUpdateApplier : IPlaybackUpdateApplier
{
    public const string STATUS_LOCAL_PLAYING = "Local (Playing)";
    public const string STATUS_LOCAL_PAUSED = "Local (Paused)";
    public const string STATUS_LOCAL_IDLE = "Local (Idle)";
    public const string STATUS_PUBLISHER_OFFLINE = "Publisher offline";

    private readonly IPlaybackSessionRegistry _registry;
    private readonly IHubContext<PlaybackHub, IPlaybackClient> _hubContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlaybackUpdateApplier> _logger;
    private readonly ConcurrentDictionary<string, string> _lastTrackIdBySession = new(StringComparer.Ordinal);

    public PlaybackUpdateApplier(
        IPlaybackSessionRegistry registry,
        IHubContext<PlaybackHub, IPlaybackClient> hubContext,
        TimeProvider timeProvider,
        ILogger<PlaybackUpdateApplier> logger)
    {
        _registry = registry;
        _hubContext = hubContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ApplyAsync(
        PlaybackUpdate update,
        ILyricsProvider lyricsProvider,
        ILyricsCacheRepository lyricsCache,
        CancellationToken cancellationToken = default)
    {
        string sessionId = update.SessionId;
        PlaybackState state = update.State;
        IPlaybackClient viewers = _hubContext.Clients.Viewers(sessionId);

        if (state.CurrentTrack is not { } track)
        {
            _lastTrackIdBySession.TryRemove(sessionId, out _);
            cancellationToken.ThrowIfCancellationRequested();
            _registry.UpdateUserState(sessionId, update.DisplayName, null, null, 0);

            await viewers.ReceivePlaybackState(state.ToDto(sessionId, update.DisplayName));
            await viewers.ReceiveDiagnostics(CreateDiagnostics(sessionId, update.DisplayName, STATUS_LOCAL_IDLE));
            return;
        }

        // Pushed updates detect a track change in any play state, so a player paused on a new
        // track still gets its lyrics.
        bool trackChanged = !_lastTrackIdBySession.TryGetValue(sessionId, out string? previousTrackId)
            || !string.Equals(previousTrackId, track.Id, StringComparison.Ordinal);

        if (trackChanged)
        {
            _logger.LogInformation(
                "Track changed for Session {SessionRef}: {Artist} - {Title}",
                SessionLogRef.For(sessionId),
                track.Artist,
                track.Title);

            SyncedLyrics? lyrics = update.AllowLyricsFetch
                ? await FetchLyricsAsync(track, lyricsProvider, sessionId, cancellationToken)
                : null;
            int trackOffset = await lyricsCache.GetTrackOffsetAsync(track.Id, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _lastTrackIdBySession[sessionId] = track.Id;
            _registry.UpdateUserState(sessionId, update.DisplayName, state, lyrics, trackOffset);

            await viewers.ReceiveLyrics(track.ToLyricsDto(lyrics));
            await viewers.ReceiveTrackOffset(new TrackOffsetDto { TrackId = track.Id, OffsetMs = trackOffset });
        }
        else
        {
            // Keeps the lyrics and offset the snapshot holds now, so an override or offset set
            // since the last update is not reverted.
            cancellationToken.ThrowIfCancellationRequested();
            _registry.UpdateUserPlayback(sessionId, update.DisplayName, state);
        }

        await viewers.ReceivePlaybackState(state.ToDto(sessionId, update.DisplayName));
        await viewers.ReceiveDiagnostics(CreateDiagnostics(
            sessionId,
            update.DisplayName,
            state.IsPlaying ? STATUS_LOCAL_PLAYING : STATUS_LOCAL_PAUSED));
    }

    public async Task MarkPublisherOfflineAsync(
        string sessionId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        UserPlaybackSnapshot? snapshot = _registry.GetUserState(sessionId);
        IPlaybackClient viewers = _hubContext.Clients.Viewers(sessionId);

        if (snapshot?.PlaybackState is { CurrentTrack: not null } state)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            PlaybackState frozen = state with
            {
                IsPlaying = false,
                Progress = ProjectProgress(state, now),
                TimestampUtc = now
            };

            cancellationToken.ThrowIfCancellationRequested();
            _registry.UpdateUserPlayback(sessionId, snapshot.DisplayName, frozen);
            await viewers.ReceivePlaybackState(frozen.ToDto(sessionId, snapshot.DisplayName));
            displayName = snapshot.DisplayName;
        }

        await viewers.ReceiveDiagnostics(CreateDiagnostics(sessionId, displayName, STATUS_PUBLISHER_OFFLINE));
    }

    public void Forget(string sessionId)
    {
        _lastTrackIdBySession.TryRemove(sessionId, out _);
    }

    private static TimeSpan ProjectProgress(PlaybackState state, DateTimeOffset now)
    {
        TimeSpan progress = state.Progress;
        if (state.IsPlaying && now > state.TimestampUtc)
        {
            progress += now - state.TimestampUtc;
        }

        TimeSpan duration = state.CurrentTrack?.Duration ?? TimeSpan.Zero;
        return duration > TimeSpan.Zero && progress > duration ? duration : progress;
    }

    private async Task<SyncedLyrics?> FetchLyricsAsync(
        TrackInfo track,
        ILyricsProvider lyricsProvider,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await lyricsProvider.GetLyricsAsync(track, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Lyrics lookup failed for Session {SessionRef}; broadcasting the track without lyrics",
                SessionLogRef.For(sessionId));
            return null;
        }
    }

    private DiagnosticsDto CreateDiagnostics(string sessionId, string displayName, string status)
    {
        return new DiagnosticsDto
        {
            ConnectedClients = _registry.ConnectedClientsCount,
            AuthorizedSessions = 1,
            PollerStatus = status,
            ActiveUserId = sessionId,
            ActiveUserName = displayName,
            ServerTimeUtc = _timeProvider.GetUtcNow()
        };
    }
}
