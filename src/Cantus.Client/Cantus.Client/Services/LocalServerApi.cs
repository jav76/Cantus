using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cantus.Client.Services;

public interface ILocalServerApi
{
    /// <summary>The version of the Cantus server answering at the base URL, or null when none answers.</summary>
    Task<string?> GetVersionAsync(string baseUrl, CancellationToken cancellationToken = default);

    /// <summary>Asks a server this app started to stop. False when it refuses or does not answer.</summary>
    Task<bool> RequestShutdownAsync(string baseUrl, string controlToken, CancellationToken cancellationToken = default);
}

public sealed class LocalServerApi : ILocalServerApi
{
    public const string SHUTDOWN_PATH = "/api/local-server/shutdown";
    public const string CONTROL_TOKEN_HEADER = "X-Cantus-Local-Token";

    public static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(3);

    private const string VERSION_PROPERTY = "version";

    private readonly HttpClient _httpClient;

    public LocalServerApi(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> GetVersionAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TIMEOUT);
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"{baseUrl}{ServerAddress.HEALTH_PATH}",
                timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            using JsonDocument document = JsonDocument.Parse(body);

            // A degraded server answers 503 with the same body, and is still a running Cantus server.
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(VERSION_PROPERTY, out JsonElement version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
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

    public async Task<bool> RequestShutdownAsync(string baseUrl, string controlToken, CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TIMEOUT);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}{SHUTDOWN_PATH}");
            request.Headers.Add(CONTROL_TOKEN_HEADER, controlToken);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, timeout.Token);

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
