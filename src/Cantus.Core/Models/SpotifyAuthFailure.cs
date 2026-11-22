namespace Cantus.Core.Models;

/// <summary>Why a Spotify sign-in did not complete, in terms the sign-in page can explain.</summary>
public enum SpotifyAuthFailure
{
    Unknown,
    Cancelled,
    NotConfigured,
    InvalidClientId,
    UserClientIdsDisabled,
    NotAllowlisted,
    OwnerNeedsPremium,
    InvalidGrant,
    SignInExpired
}

/// <summary>Neither the server nor the user supplied a Spotify Client ID.</summary>
public sealed class SpotifyNotConfiguredException : InvalidOperationException
{
    public SpotifyNotConfiguredException()
        : base("No Spotify Client ID is configured on the server and none was supplied.")
    {
    }
}
