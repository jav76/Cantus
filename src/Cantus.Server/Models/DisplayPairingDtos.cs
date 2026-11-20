namespace Cantus.Server.Models;

/// <summary>Starts a pairing for the hub connection that should receive the Display token.</summary>
public sealed record DisplayPairingRequestDto
{
    public string? ConnectionId { get; init; }
}

/// <summary>The code a waiting Display shows, and the id it matches the approval against.</summary>
public sealed record DisplayPairingDto
{
    public required string PairingId { get; init; }
    public required string Code { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>An owner approves a pairing code and names the new Display.</summary>
public sealed record DisplayPairingApprovalDto
{
    public string? Code { get; init; }
    public string? Name { get; init; }
}

public sealed record DisplayRenameDto
{
    public string? Name { get; init; }
}

/// <summary>
/// Sent to the waiting Display's hub connection once its code is approved. <see cref="Token"/>
/// is the only copy of the Display token.
/// </summary>
public sealed record DisplayPairedDto
{
    public required string PairingId { get; init; }
    public required string DisplayId { get; init; }
    public required string Name { get; init; }
    public required string Token { get; init; }

    public override string ToString() =>
        $"DisplayPairedDto {{ PairingId = {PairingId}, DisplayId = {DisplayId}, Name = {Name}, Token = [REDACTED] }}";
}
