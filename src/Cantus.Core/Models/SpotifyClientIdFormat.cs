using System.Diagnostics.CodeAnalysis;

namespace Cantus.Core.Models;

/// <summary>A Spotify app Client ID: 32 hexadecimal characters.</summary>
public static class SpotifyClientIdFormat
{
    public const int LENGTH = 32;

    public static bool TryNormalize(string? candidate, [NotNullWhen(true)] out string? clientId)
    {
        clientId = null;
        string trimmed = candidate?.Trim() ?? string.Empty;
        if (trimmed.Length != LENGTH || !trimmed.All(Uri.IsHexDigit))
        {
            return false;
        }

        clientId = trimmed.ToLowerInvariant();
        return true;
    }
}
