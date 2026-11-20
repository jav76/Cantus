using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

/// <summary>
/// Calls the lyrics correction REST endpoints on the server this client is
/// connected to. Sends the Session token as a bearer token when the client
/// holds one; the browser head also sends its Session cookie.
/// </summary>
public sealed class LyricsCorrectionService : ILyricsCorrectionService
{
    // Listing candidates waits on every enabled provider, so allow more than a hub call.
    private const int REQUEST_TIMEOUT_SECONDS = 30;

    private readonly ISignalRPlaybackClient _client;
    private readonly HttpClient _httpClient;
    private readonly ILogger<LyricsCorrectionService> _logger;

    public LyricsCorrectionService(ISignalRPlaybackClient client)
        : this(client, new HttpClient { Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS) })
    {
    }

    public LyricsCorrectionService(ISignalRPlaybackClient client, HttpClient httpClient)
        : this(client, httpClient, ClientLoggingManager.CreateLogger<LyricsCorrectionService>())
    {
    }

    public LyricsCorrectionService(
        ISignalRPlaybackClient client,
        HttpClient httpClient,
        ILogger<LyricsCorrectionService> logger)
    {
        _client = client;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<LyricsCandidatesResult> GetCandidatesAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, trackId, "candidates");
        (LyricsCorrectionStatus status, LyricsCandidatesPayload? payload) = await SendAsync(
            request,
            CantusJsonContext.Default.LyricsCandidatesPayload,
            cancellationToken);
        return new LyricsCandidatesResult(status, payload);
    }

    public async Task<LyricsOverrideResult> ApplyOverrideAsync(
        string trackId,
        LyricsOverrideRequestPayload request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = CreateRequest(HttpMethod.Post, trackId, "override");
        message.Content = JsonContent.Create(request, CantusJsonContext.Default.LyricsOverrideRequestPayload);
        (LyricsCorrectionStatus status, LyricsPayload? lyrics) = await SendAsync(
            message,
            CantusJsonContext.Default.LyricsPayload,
            cancellationToken);
        return new LyricsOverrideResult(status, lyrics);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string trackId, string action)
    {
        string url = $"{_client.ServerBaseUrl.TrimEnd('/')}/api/lyrics/{Uri.EscapeDataString(trackId)}/{action}";
        HttpRequestMessage request = new(method, url);
        string? sessionToken = _client.SessionToken;
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }

        return request;
    }

    private async Task<(LyricsCorrectionStatus Status, T? Payload)> SendAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Lyrics correction request {Method} {Path} failed with {StatusCode}",
                    request.Method,
                    request.RequestUri?.AbsolutePath,
                    (int)response.StatusCode);
                return (MapFailure(response.StatusCode), null);
            }

            T? payload = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
            return payload is null ? (LyricsCorrectionStatus.Failed, null) : (LyricsCorrectionStatus.Succeeded, payload);
        }
        catch (Exception ex) when (
            (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
            && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Lyrics correction request {Method} {Path} could not complete",
                request.Method,
                request.RequestUri?.AbsolutePath);
            return (LyricsCorrectionStatus.Failed, null);
        }
    }

    private static LyricsCorrectionStatus MapFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => LyricsCorrectionStatus.SignedOut,
        HttpStatusCode.Forbidden => LyricsCorrectionStatus.NotAllowed,
        HttpStatusCode.NotFound => LyricsCorrectionStatus.NotFound,
        HttpStatusCode.BadGateway => LyricsCorrectionStatus.ProviderUnavailable,
        _ => LyricsCorrectionStatus.Failed
    };
}
