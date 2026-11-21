namespace Cantus.Client.Models;

/// <summary>Where this client stands in becoming a paired Display.</summary>
public enum DisplayPairingState
{
    /// <summary>No Display token and the pairing screen is closed.</summary>
    Unpaired,

    /// <summary>The pairing screen is open and a code is being requested (or waits for the hub connection).</summary>
    RequestingCode,

    /// <summary>A code is shown and waits for an owner to approve it.</summary>
    ShowingCode,

    /// <summary>The shown code ran out; a new one is being requested.</summary>
    CodeExpired,

    /// <summary>The last code request failed; another is scheduled.</summary>
    RequestFailed,

    /// <summary>This client holds a Display token and shows its owner's playback.</summary>
    Paired,
}
