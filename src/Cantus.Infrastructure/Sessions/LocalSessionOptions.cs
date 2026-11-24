using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Cantus.Infrastructure.Sessions;

/// <summary>
/// Operator settings for Local Sessions: Sessions with no Spotify account, created by a desktop
/// client that publishes what the computer's own players are playing.
/// </summary>
public sealed class LocalSessionOptions : IValidatableObject
{
    public const string SECTION_NAME = "LocalSessions";
    public const string RATE_LIMIT_POLICY = "local-session-create";

    public const int MIN_JOIN_CODE_LENGTH = 8;
    public const int MAX_RATE_LIMIT_WINDOW_SECONDS = 86400;
    public const int MAX_IDLE_EXPIRY_DAYS = 3650;
    public const int MAX_PUBLISHER_GRACE_SECONDS = 600;

    public bool Enabled { get; set; }

    /// <summary>
    /// When set, creating a Local Session requires this code. Blank means no code. A set code
    /// must be at least <see cref="MIN_JOIN_CODE_LENGTH"/> characters.
    /// </summary>
    public string? JoinCode { get; set; }

    /// <summary>Local Session creations allowed per client address per window.</summary>
    [Range(1, int.MaxValue)]
    public int RateLimitPermitLimit { get; set; } = 5;

    [Range(1, MAX_RATE_LIMIT_WINDOW_SECONDS)]
    public int RateLimitWindowSeconds { get; set; } = 600;

    /// <summary>Upper bound on stored Local Sessions, checked after idle ones are pruned.</summary>
    [Range(1, int.MaxValue)]
    public int MaxSessions { get; set; } = 500;

    /// <summary>A Local Session unused for this many days is deleted, along with its Displays.</summary>
    [Range(1, MAX_IDLE_EXPIRY_DAYS)]
    public int IdleExpiryDays { get; set; } = 90;

    /// <summary>
    /// Seconds a Session's playback stays live after its publisher disconnects, before it is
    /// shown paused. Covers the client's reconnect schedule.
    /// </summary>
    [Range(1, MAX_PUBLISHER_GRACE_SECONDS)]
    public int PublisherGraceSeconds { get; set; } = 20;

    [MemberNotNullWhen(true, nameof(JoinCode))]
    public bool RequiresJoinCode => !string.IsNullOrWhiteSpace(JoinCode);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequiresJoinCode && JoinCode.Length < MIN_JOIN_CODE_LENGTH)
        {
            yield return new ValidationResult(
                $"{nameof(JoinCode)} must be at least {MIN_JOIN_CODE_LENGTH} characters when set.",
                [nameof(JoinCode)]);
        }
    }
}
