using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

/// <summary><see cref="ILocalSessionApi"/> over HTTP against the client's Cantus server.</summary>
public sealed class LocalSessionApi : ILocalSessionApi
{
    public const string LOCAL_SESSION_PATH = "/api/auth/local";

    public const string ERROR_INVALID_DISPLAY_NAME = "invalid_display_name";
    public const string ERROR_JOIN_CODE_REQUIRED = "join_code_required";
    public const string ERROR_JOIN_CODE_INVALID = "join_code_invalid";
    public const string ERROR_DISABLED = "local_sessions_disabled";
    public const string ERROR_LIMIT = "local_session_limit";

    private static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<LocalSessionApi>();

    private readonly ISignalRPlaybackClient _client;
    private readonly HttpClient _http;

    public LocalSessionApi(ISignalRPlaybackClient client, HttpClient? http = null)
    {
        _client = client;
        _http = http ?? new HttpClient { Timeout = REQUEST_TIMEOUT };
    }

    public async Task<LocalSessionResult> CreateAsync(
        string displayName,
        string? joinCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LocalSessionRequestPayload request = new()
            {
                DisplayName = displayName,
                JoinCode = string.IsNullOrWhiteSpace(joinCode) ? null : joinCode.Trim(),
            };
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                $"{_client.ServerBaseUrl}{LOCAL_SESSION_PATH}",
                request,
                CantusJsonContext.Default.LocalSessionRequestPayload,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                AuthorizedSessionPayload? session = await response.Content.ReadFromJsonAsync(
                    CantusJsonContext.Default.AuthorizedSessionPayload,
                    cancellationToken);
                return session is not null && !string.IsNullOrWhiteSpace(session.Id)
                    ? new LocalSessionResult(LocalSessionStatus.Created, session)
                    : new LocalSessionResult(LocalSessionStatus.Failed);
            }

            string? error = await ReadErrorAsync(response, cancellationToken);
            LocalSessionStatus status = MapRefusal(response.StatusCode, error);
            _logger.LogWarning(
                "Local Session request was refused with {StatusCode} {Error}",
                (int)response.StatusCode,
                error);
            return new LocalSessionResult(status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Local Session request failed");
            return new LocalSessionResult(LocalSessionStatus.Failed);
        }
    }

    internal static LocalSessionStatus MapRefusal(HttpStatusCode statusCode, string? error) => statusCode switch
    {
        HttpStatusCode.BadRequest => LocalSessionStatus.InvalidDisplayName,
        HttpStatusCode.Unauthorized when error == ERROR_JOIN_CODE_REQUIRED => LocalSessionStatus.JoinCodeRequired,
        HttpStatusCode.Unauthorized when error == ERROR_JOIN_CODE_INVALID => LocalSessionStatus.JoinCodeInvalid,
        HttpStatusCode.Forbidden => LocalSessionStatus.Disabled,
        HttpStatusCode.TooManyRequests => LocalSessionStatus.RateLimited,
        HttpStatusCode.ServiceUnavailable when error == ERROR_LIMIT => LocalSessionStatus.LimitReached,
        _ => LocalSessionStatus.Failed,
    };

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ApiErrorPayload? body = await response.Content.ReadFromJsonAsync(
                CantusJsonContext.Default.ApiErrorPayload,
                cancellationToken);
            return body?.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
