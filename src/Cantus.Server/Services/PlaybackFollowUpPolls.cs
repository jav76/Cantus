using Cantus.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Cantus.Server.Services;

/// <summary>
/// Two short follow-up polls after an accepted player command, so every
/// connected client sees the committed state quickly instead of waiting for
/// the next adaptive poll seconds later.
/// </summary>
public static class PlaybackFollowUpPolls
{
    // Spotify's API is eventually consistent: a poll fired immediately after
    // an accepted command often still returns the previous state. The first
    // follow-up gives Spotify time to commit the change; the second is a
    // safety net for slower propagation.
    private const int FIRST_FOLLOW_UP_POLL_DELAY_MS = 400;
    private const int SECOND_FOLLOW_UP_POLL_DELAY_MS = 1100;

    public static void Schedule(IPlaybackSessionRegistry registry, string userId, ILogger logger)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(FIRST_FOLLOW_UP_POLL_DELAY_MS));
                registry.RequestUserActivity(userId);
                await Task.Delay(TimeSpan.FromMilliseconds(SECOND_FOLLOW_UP_POLL_DELAY_MS));
                registry.RequestUserActivity(userId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Follow-up playback poll failed for Session {SessionRef}", SessionLogRef.For(userId));
            }
        });
    }
}
