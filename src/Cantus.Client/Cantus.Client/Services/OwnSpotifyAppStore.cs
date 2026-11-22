using Cantus.Core.Models;

namespace Cantus.Client.Services;

/// <summary>The Client ID of the user's own Spotify app, remembered per device for the next sign-in.</summary>
public sealed class OwnSpotifyAppStore
{
    public const string CLIENT_ID_KEY = "cantus_spotify_own_client_id";

    private readonly ISettingsStore _store;

    public OwnSpotifyAppStore(ISettingsStore store)
    {
        _store = store;
    }

    /// <summary>The saved Client ID, or null when none is saved or the saved value is not a Client ID.</summary>
    public string? ClientId =>
        SpotifyClientIdFormat.TryNormalize(_store.GetValue(CLIENT_ID_KEY) as string, out string? clientId)
            ? clientId
            : null;

    /// <summary>Saves a valid Client ID and returns true; anything else is ignored.</summary>
    public bool Save(string? clientId)
    {
        if (!SpotifyClientIdFormat.TryNormalize(clientId, out string? normalized))
        {
            return false;
        }

        _store.SetValue(CLIENT_ID_KEY, normalized);
        return true;
    }

    public void Forget() => _store.SetValue(CLIENT_ID_KEY, string.Empty);
}
