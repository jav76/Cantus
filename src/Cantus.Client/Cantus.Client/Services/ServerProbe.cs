using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cantus.Client.Services;

public interface IServerProbe
{
    /// <summary>Whether a Cantus server answers its health check at the base URL. Never throws for an unreachable server.</summary>
    Task<bool> IsReachableAsync(string baseUrl, CancellationToken cancellationToken = default);
}

/// <summary>Checks a server address with <c>GET /api/health</c>.</summary>
public sealed class ServerProbe : IServerProbe
{
    public static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;

    public ServerProbe(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> IsReachableAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TIMEOUT);
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"{baseUrl}{ServerAddress.HEALTH_PATH}",
                timeout.Token);

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
