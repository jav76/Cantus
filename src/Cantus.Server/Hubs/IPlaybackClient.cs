using Cantus.Server.Models;

namespace Cantus.Server.Hubs;

public interface IPlaybackClient
{
    Task ReceivePlaybackState(PlaybackStateDto state);
    Task ReceiveLyrics(LyricsDto lyrics);
    Task ReceiveTrackOffset(TrackOffsetDto offset);
    Task ReceiveSessions(IReadOnlyList<AuthorizedSessionDto> sessions);
    Task ReceiveAuthSession(AuthorizedSessionDto session);
    Task ReceiveSessionRevoked(string userId);
    Task ReceiveDiagnostics(DiagnosticsDto diagnostics);
    Task ReceiveClockSync(ClockSyncResponse response);

    /// <summary>Delivers a new Display token to the connection that requested the pairing.</summary>
    Task ReceiveDisplayPaired(DisplayPairedDto pairing);

    /// <summary>Tells a Display connection its token was revoked, just before it is closed.</summary>
    Task ReceiveDisplayRevoked(string displayId);
}
