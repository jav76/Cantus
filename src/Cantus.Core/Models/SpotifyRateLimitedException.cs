using System.Globalization;

namespace Cantus.Core.Models;

/// <summary>
/// A Spotify call was refused or not sent because Spotify asked the server to
/// back off. <see cref="RetryAfter"/> is how long the pause still lasts.
/// </summary>
public sealed class SpotifyRateLimitedException : Exception
{
    public SpotifyRateLimitedException(TimeSpan retryAfter, Exception? innerException = null)
        : base(
            string.Format(
                CultureInfo.InvariantCulture,
                "Spotify calls are paused for {0:F0} s.",
                retryAfter.TotalSeconds),
            innerException)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; }
}
