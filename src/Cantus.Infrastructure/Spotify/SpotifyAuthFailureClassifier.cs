using System.Net;
using Cantus.Core.Models;
using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

/// <summary>
/// Turns what Spotify returns during sign-in (the callback's <c>error</c> value, or a failed
/// token or profile request) into a <see cref="SpotifyAuthFailure"/>. Spotify reports most of
/// these only as message text, so matching is by keyword and anything else is
/// <see cref="SpotifyAuthFailure.Unknown"/>.
/// </summary>
public static class SpotifyAuthFailureClassifier
{
    public static SpotifyAuthFailure FromCallbackError(string? error)
    {
        return string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
            ? SpotifyAuthFailure.Cancelled
            : SpotifyAuthFailure.Unknown;
    }

    public static SpotifyAuthFailure FromException(Exception exception)
    {
        if (exception is SpotifyNotConfiguredException)
        {
            return SpotifyAuthFailure.NotConfigured;
        }

        if (exception is not APIException apiException)
        {
            return SpotifyAuthFailure.Unknown;
        }

        string text = $"{apiException.Message} {apiException.Response?.Body}";
        HttpStatusCode? status = apiException.Response?.StatusCode;

        if (Contains(text, "premium"))
        {
            return SpotifyAuthFailure.OwnerNeedsPremium;
        }

        if (Contains(text, "not registered") || Contains(text, "developer dashboard"))
        {
            return SpotifyAuthFailure.NotAllowlisted;
        }

        if (Contains(text, "invalid_client") || Contains(text, "invalid client"))
        {
            return SpotifyAuthFailure.InvalidClientId;
        }

        if (Contains(text, "invalid_grant") || Contains(text, "invalid authorization code"))
        {
            return SpotifyAuthFailure.InvalidGrant;
        }

        return status == HttpStatusCode.Forbidden
            ? SpotifyAuthFailure.NotAllowlisted
            : SpotifyAuthFailure.Unknown;
    }

    private static bool Contains(string text, string keyword) =>
        text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
