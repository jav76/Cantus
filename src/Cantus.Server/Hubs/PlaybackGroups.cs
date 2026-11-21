using Cantus.Server.Models;
using Microsoft.AspNetCore.SignalR;

namespace Cantus.Server.Hubs;

/// <summary>
/// SignalR groups for one owner's playback. Session connections join the owner group and
/// Display connections join the display group, so payloads that carry the owner's Session
/// id (which is itself a credential) never reach a Display.
/// </summary>
public static class PlaybackGroups
{
    public static string Owner(string ownerSessionId) => $"user_{ownerSessionId}";

    public static string Displays(string ownerSessionId) => $"display_{ownerSessionId}";

    /// <summary>
    /// Everyone watching an owner's playback: their Session connections and their Displays.
    /// Displays get the same playback, lyrics, offsets and diagnostics without the Session id,
    /// and never receive session lists or revocations.
    /// </summary>
    public static IPlaybackClient Viewers(this IHubClients<IPlaybackClient> clients, string ownerSessionId)
    {
        return new ViewersClient(clients.Group(Owner(ownerSessionId)), clients.Group(Displays(ownerSessionId)));
    }

    public static PlaybackStateDto ForDisplay(this PlaybackStateDto state) => state with { ActiveUserId = null };

    public static DiagnosticsDto ForDisplay(this DiagnosticsDto diagnostics) =>
        diagnostics with { ActiveUserId = null };

    private sealed class ViewersClient : IPlaybackClient
    {
        private readonly IPlaybackClient _owners;
        private readonly IPlaybackClient _displays;

        public ViewersClient(IPlaybackClient owners, IPlaybackClient displays)
        {
            _owners = owners;
            _displays = displays;
        }

        public Task ReceivePlaybackState(PlaybackStateDto state) =>
            Task.WhenAll(_owners.ReceivePlaybackState(state), _displays.ReceivePlaybackState(state.ForDisplay()));

        public Task ReceiveLyrics(LyricsDto lyrics) =>
            Task.WhenAll(_owners.ReceiveLyrics(lyrics), _displays.ReceiveLyrics(lyrics));

        public Task ReceiveTrackOffset(TrackOffsetDto offset) =>
            Task.WhenAll(_owners.ReceiveTrackOffset(offset), _displays.ReceiveTrackOffset(offset));

        public Task ReceiveDiagnostics(DiagnosticsDto diagnostics) =>
            Task.WhenAll(_owners.ReceiveDiagnostics(diagnostics), _displays.ReceiveDiagnostics(diagnostics.ForDisplay()));

        public Task ReceiveClockSync(ClockSyncResponse response) =>
            Task.WhenAll(_owners.ReceiveClockSync(response), _displays.ReceiveClockSync(response));

        public Task ReceiveSessions(IReadOnlyList<AuthorizedSessionDto> sessions) => _owners.ReceiveSessions(sessions);

        public Task ReceiveAuthSession(AuthorizedSessionDto session) => _owners.ReceiveAuthSession(session);

        public Task ReceiveSessionRevoked(string userId) => _owners.ReceiveSessionRevoked(userId);

        public Task ReceiveDisplaysChanged() => _owners.ReceiveDisplaysChanged();

        public Task ReceiveDisplayPaired(DisplayPairedDto pairing) => NotForViewers();

        public Task ReceiveDisplayRevoked(string displayId) => NotForViewers();

        private static Task NotForViewers() =>
            Task.FromException(new NotSupportedException("Display pairing events target single connections."));
    }
}
