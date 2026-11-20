using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public sealed class DisplayPairingRequestPayload
{
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = string.Empty;
}

public sealed class DisplayPairingCodePayload
{
    [JsonPropertyName("pairingId")]
    public string PairingId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary><see cref="IDisplayPairingApi"/> over HTTP against the client's Cantus server.</summary>
public sealed class DisplayPairingApi : IDisplayPairingApi
{
    public const string PAIRINGS_PATH = "/api/displays/pairings";

    private static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<DisplayPairingApi>();

    private readonly ISignalRPlaybackClient _client;
    private readonly HttpClient _http;

    public DisplayPairingApi(ISignalRPlaybackClient client, HttpClient? http = null)
    {
        _client = client;
        _http = http ?? new HttpClient { Timeout = REQUEST_TIMEOUT };
    }

    public async Task<DisplayPairingRequestResult> RequestPairingAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            DisplayPairingRequestPayload request = new() { ConnectionId = connectionId };
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                $"{_client.ServerBaseUrl}{PAIRINGS_PATH}",
                request,
                CantusJsonContext.Default.DisplayPairingRequestPayload,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                DisplayPairingCodePayload? pairing = await response.Content.ReadFromJsonAsync(
                    CantusJsonContext.Default.DisplayPairingCodePayload,
                    cancellationToken);

                return pairing is not null && !string.IsNullOrWhiteSpace(pairing.Code)
                    ? new DisplayPairingRequestResult(DisplayPairingRequestStatus.Created, pairing)
                    : new DisplayPairingRequestResult(DisplayPairingRequestStatus.Failed);
            }

            _logger.LogWarning("Display pairing request was refused with {StatusCode}", (int)response.StatusCode);

            DisplayPairingRequestStatus status = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => DisplayPairingRequestStatus.RateLimited,
                HttpStatusCode.ServiceUnavailable => DisplayPairingRequestStatus.Unavailable,
                _ => DisplayPairingRequestStatus.Failed,
            };
            return new DisplayPairingRequestResult(status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Display pairing request failed");
            return new DisplayPairingRequestResult(DisplayPairingRequestStatus.Failed);
        }
    }
}
