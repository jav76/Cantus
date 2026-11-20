using System.ComponentModel.DataAnnotations;

namespace Cantus.Server.Services;

public sealed class DisplayPairingOptions
{
    public const string SECTION_NAME = "DisplayPairing";
    public const string REQUEST_RATE_LIMIT_POLICY = "display-pairing-request";
    public const string APPROVE_RATE_LIMIT_POLICY = "display-pairing-approve";

    public const int MIN_CODE_LENGTH = 6;
    public const int MAX_CODE_LENGTH = 16;
    public const int MIN_CODE_LIFETIME_SECONDS = 30;
    public const int MAX_CODE_LIFETIME_SECONDS = 3600;
    public const int MAX_RATE_LIMIT_WINDOW_SECONDS = 3600;

    /// <summary>Length of a pairing code. Codes use an alphabet without look-alike characters.</summary>
    [Range(MIN_CODE_LENGTH, MAX_CODE_LENGTH)]
    public int CodeLength { get; set; } = 8;

    /// <summary>Seconds a pairing code stays valid after it is requested.</summary>
    [Range(MIN_CODE_LIFETIME_SECONDS, MAX_CODE_LIFETIME_SECONDS)]
    public int CodeLifetimeSeconds { get; set; } = 300;

    /// <summary>Upper bound on pairings waiting for approval, across all clients.</summary>
    [Range(1, int.MaxValue)]
    public int MaxPendingPairings { get; set; } = 1000;

    /// <summary>
    /// Requests allowed per client address per window, counted separately for pairing requests
    /// and for approvals. IPv6 addresses in the same /64 count as one client.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int RateLimitPermitLimit { get; set; } = 10;

    [Range(1, MAX_RATE_LIMIT_WINDOW_SECONDS)]
    public int RateLimitWindowSeconds { get; set; } = 60;
}
