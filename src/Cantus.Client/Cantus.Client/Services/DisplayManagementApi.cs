using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

/// <summary>A Display as its owner sees it: never carries the Display token.</summary>
public sealed class DisplayInfoPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    [JsonPropertyName("lastSeenAtUtc")]
    public DateTimeOffset? LastSeenAtUtc { get; set; }
}

public sealed class DisplayApprovalRequestPayload
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class DisplayRenameRequestPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// <see cref="IDisplayManagementApi"/> over HTTP against the client's Cantus server. The
/// Session token goes in the <c>Authorization</c> header when the client holds one; the web
/// app otherwise relies on the browser sending the Session cookie.
/// </summary>
public sealed class DisplayManagementApi : IDisplayManagementApi
{
    public const string DISPLAYS_PATH = "/api/displays";
    public const string APPROVE_PATH = "/api/displays/pairings/approve";

    private const string BEARER_SCHEME = "Bearer";

    private static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly ILogger _logger = ClientLoggingManager.CreateLogger<DisplayManagementApi>();

    private readonly ISignalRPlaybackClient _client;
    private readonly HttpClient _http;

    public DisplayManagementApi(ISignalRPlaybackClient client, HttpClient? http = null)
    {
        _client = client;
        _http = http ?? new HttpClient { Timeout = REQUEST_TIMEOUT };
    }

    public async Task<DisplayApprovalResult> ApproveAsync(
        string code,
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            DisplayApprovalRequestPayload body = new() { Code = code, Name = name };
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, APPROVE_PATH);
            request.Content = JsonContent.Create(body, CantusJsonContext.Default.DisplayApprovalRequestPayload);

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new DisplayApprovalResult(MapFailure(response.StatusCode, "approve"));
            }

            DisplayInfoPayload? display = await response.Content.ReadFromJsonAsync(
                CantusJsonContext.Default.DisplayInfoPayload,
                cancellationToken);
            return new DisplayApprovalResult(DisplayManagementStatus.Succeeded, display);
        }
        catch (Exception ex) when (IsRequestFailure(ex))
        {
            _logger.LogWarning(ex, "Display approval request failed");
            return new DisplayApprovalResult(DisplayManagementStatus.Failed);
        }
    }

    public async Task<DisplayListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, DISPLAYS_PATH);
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new DisplayListResult(MapFailure(response.StatusCode, "list"));
            }

            List<DisplayInfoPayload>? displays = await response.Content.ReadFromJsonAsync(
                CantusJsonContext.Default.ListDisplayInfoPayload,
                cancellationToken);
            return new DisplayListResult(DisplayManagementStatus.Succeeded, displays ?? new List<DisplayInfoPayload>());
        }
        catch (Exception ex) when (IsRequestFailure(ex))
        {
            _logger.LogWarning(ex, "Display list request failed");
            return new DisplayListResult(DisplayManagementStatus.Failed);
        }
    }

    public async Task<DisplayManagementStatus> RenameAsync(
        string displayId,
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            DisplayRenameRequestPayload body = new() { Name = name };
            using HttpRequestMessage request = CreateRequest(HttpMethod.Patch, DisplayPath(displayId));
            request.Content = JsonContent.Create(body, CantusJsonContext.Default.DisplayRenameRequestPayload);

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? DisplayManagementStatus.Succeeded
                : MapFailure(response.StatusCode, "rename");
        }
        catch (Exception ex) when (IsRequestFailure(ex))
        {
            _logger.LogWarning(ex, "Display rename request failed for {DisplayId}", displayId);
            return DisplayManagementStatus.Failed;
        }
    }

    public async Task<DisplayManagementStatus> RevokeAsync(
        string displayId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, DisplayPath(displayId));
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? DisplayManagementStatus.Succeeded
                : MapFailure(response.StatusCode, "revoke");
        }
        catch (Exception ex) when (IsRequestFailure(ex))
        {
            _logger.LogWarning(ex, "Display revoke request failed for {DisplayId}", displayId);
            return DisplayManagementStatus.Failed;
        }
    }

    private static string DisplayPath(string displayId) => $"{DISPLAYS_PATH}/{Uri.EscapeDataString(displayId)}";

    // A relative or malformed server address surfaces as InvalidOperationException or UriFormatException.
    private static bool IsRequestFailure(Exception ex) => ex is HttpRequestException
        or TaskCanceledException
        or JsonException
        or InvalidOperationException
        or UriFormatException
        or NotSupportedException;

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, $"{_client.ServerBaseUrl}{path}");
        string? sessionToken = _client.SessionToken;
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BEARER_SCHEME, sessionToken);
        }
        return request;
    }

    private static DisplayManagementStatus MapFailure(HttpStatusCode statusCode, string operation)
    {
        _logger.LogWarning(
            "Display {Operation} request was refused with {StatusCode}",
            operation,
            (int)statusCode);

        return statusCode switch
        {
            HttpStatusCode.BadRequest => DisplayManagementStatus.InvalidName,
            HttpStatusCode.Unauthorized => DisplayManagementStatus.NotSignedIn,
            HttpStatusCode.Forbidden => DisplayManagementStatus.Forbidden,
            HttpStatusCode.NotFound => DisplayManagementStatus.NotFound,
            HttpStatusCode.TooManyRequests => DisplayManagementStatus.RateLimited,
            _ => DisplayManagementStatus.Failed,
        };
    }
}
