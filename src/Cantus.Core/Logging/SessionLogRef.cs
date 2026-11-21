using System.Security.Cryptography;
using System.Text;

namespace Cantus.Core.Logging;

/// <summary>
/// The form in which a Session id may appear in logs. A Session id is the value of the
/// session cookie, so it is a bearer credential and must never be logged raw. The reference
/// is a short SHA-256 prefix: stable, so log lines for one Session can be correlated, but
/// not reversible to the id itself.
/// </summary>
public static class SessionLogRef
{
    public const int LENGTH = 12;
    public const string NONE = "none";

    public static string For(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return NONE;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexStringLower(hash)[..LENGTH];
    }
}
