using System;

namespace Cantus.Client.Models;

/// <summary>The colour family of a status dot; the view maps it to a brush.</summary>
public enum StatusTone
{
    Healthy,
    Warning,
    Error
}

/// <summary>
/// What the header shows about the connection: a status dot with the details in its
/// tooltip, plus one alert chip only while something needs attention.
/// </summary>
public static class HeaderStatus
{
    public const string CONNECTED = "Connected";
    public const string RECONNECTING = "Reconnecting";

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

    public static StatusTone GetStatusTone(string? connectionStatus) => connectionStatus switch
    {
        CONNECTED => StatusTone.Healthy,
        RECONNECTING => StatusTone.Warning,
        _ => StatusTone.Error
    };

    /// <summary>A rate-limited poller is a warning; a connection problem keeps its status tone.</summary>
    public static StatusTone GetAlertTone(string? connectionStatus)
        => IsConnected(connectionStatus) ? StatusTone.Warning : GetStatusTone(connectionStatus);

    public static string GetTooltip(string? connectionStatus, long rttMs, string? pollerStatus)
        => $"Connection: {connectionStatus}\nRTT: {rttMs} ms\nPoller: {pollerStatus}";
}
