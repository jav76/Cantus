using System;

namespace Cantus.Client.Models;

/// <summary>
/// What the header shows about the connection: a status dot with the details in its
/// tooltip, plus one alert chip only while something needs attention.
/// </summary>
public static class HeaderStatus
{
    public const string CONNECTED = "Connected";

    /// <summary>Prefix of the poller status the server sends while Spotify rate limits the account.</summary>
    public const string RATE_LIMITED_PREFIX = "Rate Limited";

    public static bool IsConnected(string? connectionStatus)
        => string.Equals(connectionStatus, CONNECTED, StringComparison.Ordinal);

    public static bool IsPollerAbnormal(string? pollerStatus)
        => pollerStatus is not null && pollerStatus.StartsWith(RATE_LIMITED_PREFIX, StringComparison.OrdinalIgnoreCase);

    /// <summary>The chip text, or null when everything is normal. A connection problem wins over the poller.</summary>
    public static string? GetAlertText(string? connectionStatus, string? pollerStatus)
    {
        if (!IsConnected(connectionStatus))
        {
            return string.IsNullOrWhiteSpace(connectionStatus) ? "Disconnected" : connectionStatus;
        }

        return IsPollerAbnormal(pollerStatus) ? pollerStatus : null;
    }

    public static string GetTooltip(string? connectionStatus, long rttMs, string? pollerStatus)
        => $"Connection: {connectionStatus}\nRTT: {rttMs} ms\nPoller: {pollerStatus}";
}
