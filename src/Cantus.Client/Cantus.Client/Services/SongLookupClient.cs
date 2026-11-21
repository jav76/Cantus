using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Cantus.Client.Services;

/// <summary>
/// Posts song lookups and play requests to the server the playback connection points at.
/// The server URL and Session token are read per request, because both change at runtime
/// (sign-in, logout) and a connection mock in a test may not set them. Without a token the
/// request still goes out: on WASM the same-origin <c>cantus_session_id</c> cookie authenticates it.
/// </summary>
public sealed class SongLookupClient : ISongLookupClient
{
    public const string SEARCH_PATH = "/api/lookup/search";
    public const string PLAY_PATH = "/api/lookup/play";

    private const string JSON_MEDIA_TYPE = "application/json";
    private const string BEARER_SCHEME = "Bearer";

    private readonly ISignalRPlaybackClient _connection;
    private readonly HttpClient _httpClient;

    public SongLookupClient(ISignalRPlaybackClient connection, HttpClient httpClient)
    {
        _connection = connection;
        _httpClient = httpClient;
    }

    public async Task<SongLookupCallResult> SearchAsync(
        string fragment,
        string scope,
        CancellationToken cancellationToken = default)
    {
        SongLookupSearchRequestPayload request = new()
        {
            Fragment = fragment,
            Scope = scope,
        };

        (SongLookupCallStatus status, SongLookupResponsePayload? response, string? message) = await PostAsync(
            SEARCH_PATH,
            request,
            CantusJsonContext.Default.SongLookupSearchRequestPayload,
            CantusJsonContext.Default.SongLookupResponsePayload,
            cancellationToken);
        return new SongLookupCallResult(status, response, message);
    }

    public async Task<SongPlayCallResult> PlayAsync(
        SongLookupPlayRequestPayload request,
        CancellationToken cancellationToken = default)
    {
        (SongLookupCallStatus status, SongLookupPlayResponsePayload? response, string? message) = await PostAsync(
            PLAY_PATH,
            request,
            CantusJsonContext.Default.SongLookupPlayRequestPayload,
            CantusJsonContext.Default.SongLookupPlayResponsePayload,
            cancellationToken);
        return new SongPlayCallResult(status, response, message);
    }

    internal static SongLookupCallStatus MapStatusCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.OK => SongLookupCallStatus.Success,
            HttpStatusCode.BadRequest => SongLookupCallStatus.BadRequest,
            HttpStatusCode.Unauthorized => SongLookupCallStatus.Unauthorized,
            HttpStatusCode.Forbidden => SongLookupCallStatus.Forbidden,
            HttpStatusCode.TooManyRequests => SongLookupCallStatus.RateLimited,
            _ => SongLookupCallStatus.Failed
        };
    }

    private async Task<(SongLookupCallStatus, TResponse?, string?)> PostAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            string baseUrl = _connection.ServerBaseUrl.TrimEnd('/');
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}{path}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, requestType),
                Encoding.UTF8,
                JSON_MEDIA_TYPE);

            string? token = _connection.SessionToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(BEARER_SCHEME, token);
            }

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            SongLookupCallStatus status = MapStatusCode(response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            return status switch
            {
                SongLookupCallStatus.Success => ParseSuccess(body, responseType),
                SongLookupCallStatus.BadRequest => (status, null, ParseErrorMessage(body)),
                _ => (status, null, null)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return (SongLookupCallStatus.Failed, null, null);
        }
    }

    private static (SongLookupCallStatus, TResponse?, string?) ParseSuccess<TResponse>(
        string body,
        JsonTypeInfo<TResponse> responseType)
        where TResponse : class
    {
        TResponse? parsed = JsonSerializer.Deserialize(body, responseType);
        return parsed is null
            ? (SongLookupCallStatus.Failed, null, null)
            : (SongLookupCallStatus.Success, parsed, null);
    }

    private static string? ParseErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            SongLookupErrorPayload? error = JsonSerializer.Deserialize(
                body,
                CantusJsonContext.Default.SongLookupErrorPayload);
            return string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
