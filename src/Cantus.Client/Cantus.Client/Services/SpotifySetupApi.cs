using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cantus.Client.Services;

/// <summary>How the server signs in to Spotify, from <c>GET /api/auth/spotify/setup</c>.</summary>
public sealed class SpotifySetupInfoPayload
{
    public bool ServerClientIdConfigured { get; set; }
    public bool UserClientIdsAllowed { get; set; }
    public string RedirectUri { get; set; } = string.Empty;
    public bool RedirectUriAccepted { get; set; }
    public string DashboardUrl { get; set; } = string.Empty;
}

public interface ISpotifySetupApi
{
    /// <summary>Returns null when the server cannot be reached or predates the setup endpoint.</summary>
    Task<SpotifySetupInfoPayload?> GetSetupInfoAsync(CancellationToken cancellationToken = default);
}

public sealed class SpotifySetupApi : ISpotifySetupApi
{
    public const string SETUP_PATH = "/api/auth/spotify/setup";

    private readonly ISignalRPlaybackClient _connection;
    private readonly HttpClient _httpClient;

    public SpotifySetupApi(ISignalRPlaybackClient connection, HttpClient httpClient)
    {
        _connection = connection;
        _httpClient = httpClient;
    }

    public async Task<SpotifySetupInfoPayload?> GetSetupInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string baseUrl = _connection.ServerBaseUrl.TrimEnd('/');
            using HttpResponseMessage response = await _httpClient.GetAsync($"{baseUrl}{SETUP_PATH}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize(body, CantusJsonContext.Default.SpotifySetupInfoPayload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
