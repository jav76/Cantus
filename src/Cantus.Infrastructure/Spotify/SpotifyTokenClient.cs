using SpotifyAPI.Web;

namespace Cantus.Infrastructure.Spotify;

/// <summary>The Spotify calls sign-in makes: the PKCE token requests and the profile lookup.</summary>
public interface ISpotifyTokenClient
{
    Task<PKCETokenResponse> RequestTokenAsync(PKCETokenRequest request, CancellationToken cancellationToken);

    Task<PKCETokenResponse> RefreshTokenAsync(PKCETokenRefreshRequest request, CancellationToken cancellationToken);

    Task<PrivateUser> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed class SpotifyTokenClient : ISpotifyTokenClient
{
    private readonly OAuthClient _oauth = new();

    public Task<PKCETokenResponse> RequestTokenAsync(PKCETokenRequest request, CancellationToken cancellationToken) =>
        _oauth.RequestToken(request, cancellationToken);

    public Task<PKCETokenResponse> RefreshTokenAsync(PKCETokenRefreshRequest request, CancellationToken cancellationToken) =>
        _oauth.RequestToken(request, cancellationToken);

    public Task<PrivateUser> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken) =>
        new SpotifyClient(accessToken).UserProfile.Current(cancellationToken);
}
