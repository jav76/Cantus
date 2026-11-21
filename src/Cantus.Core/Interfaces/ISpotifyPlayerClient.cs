using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ISpotifyPlayerClient
{
    Task<PlaybackState?> GetCurrentPlaybackAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> PausePlaybackAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> ResumePlaybackAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SkipToNextAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SkipToPreviousAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SetVolumeAsync([Redact] string accessToken, int volumePercent, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SeekPlaybackAsync([Redact] string accessToken, long positionMs, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SetShuffleAsync([Redact] string accessToken, bool enabled, CancellationToken cancellationToken = default);

    /// <param name="repeatMode">"off", "track", or "context".</param>
    Task<PlayerCommandResult> SetRepeatAsync([Redact] string accessToken, string repeatMode, CancellationToken cancellationToken = default);
}
