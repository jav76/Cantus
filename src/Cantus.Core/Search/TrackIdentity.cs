using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cantus.Core.Search;

/// <summary>
/// Track keys. A Spotify track id is 22 base-62 characters. A track with no trusted Spotify id
/// gets a derived key from its normalised artist, title and duration, so the same song from two
/// players (or with different casing, accents or punctuation) shares lyrics, overrides and offsets.
/// </summary>
public static partial class TrackIdentity
{
    public const string LOCAL_KEY_PREFIX = "local:";

    private const int LOCAL_KEY_HEX_LENGTH = 32;
    private const char FIELD_SEPARATOR = '\u001F';

    [GeneratedRegex(@"^[A-Za-z0-9]{22}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SpotifyTrackIdPattern();

    public static bool TryParseSpotifyTrackId(string? value, [NotNullWhen(true)] out string? trackId)
    {
        if (value is not null && SpotifyTrackIdPattern().IsMatch(value))
        {
            trackId = value;
            return true;
        }

        trackId = null;
        return false;
    }

    public static bool IsLocalKey(string? trackId) =>
        trackId is not null && trackId.StartsWith(LOCAL_KEY_PREFIX, StringComparison.Ordinal);

    /// <summary>
    /// <c>local:</c> plus the first 128 bits (lowercase hex) of a SHA-256 over the first
    /// credited artist, the full title (qualifiers such as "Remastered" are kept, since they can
    /// change the lyrics timing) and the duration rounded to whole seconds.
    /// </summary>
    public static string DeriveLocalKey(string title, string artist, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(artist);

        string normalizedArtist = OrRaw(LyricTextNormalizer.ArtistKey(artist), artist);
        string normalizedTitle = OrRaw(LyricTextNormalizer.JoinTokens(LyricTextNormalizer.Tokenize(title)), title);
        long durationSeconds = (long)Math.Round(Math.Max(0, duration.TotalSeconds), MidpointRounding.AwayFromZero);

        string material = string.Concat(
            normalizedArtist,
            FIELD_SEPARATOR,
            normalizedTitle,
            FIELD_SEPARATOR,
            durationSeconds.ToString(CultureInfo.InvariantCulture));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        string hex = Convert.ToHexStringLower(hash);
        return LOCAL_KEY_PREFIX + hex[..LOCAL_KEY_HEX_LENGTH];
    }

    /// <summary>A name made only of punctuation normalises to nothing; it still needs a stable key.</summary>
    private static string OrRaw(string normalized, string raw) =>
        normalized.Length > 0 ? normalized : raw.Trim().ToLowerInvariant();
}
