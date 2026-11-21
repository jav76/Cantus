using System.Globalization;
using System.Text.Json;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Reads a provider's JSON response with a size cap, so a lookup cannot buffer
/// an unbounded body. Use with <see cref="HttpCompletionOption.ResponseHeadersRead"/>;
/// otherwise HttpClient buffers the whole body before this cap applies.
/// </summary>
internal static class ProviderResponseReader
{
    public const int MAX_LOOKUP_RESPONSE_BYTES = 1024 * 1024;

    private const int READ_CHUNK_BYTES = 16 * 1024;

    public static async Task<T?> ReadJsonAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions options,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MAX_LOOKUP_RESPONSE_BYTES)
        {
            throw TooLarge(providerName);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[READ_CHUNK_BYTES];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MAX_LOOKUP_RESPONSE_BYTES)
            {
                throw TooLarge(providerName);
            }

            buffer.Write(chunk, 0, read);
        }

        return JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options);
    }

    private static HttpRequestException TooLarge(string providerName)
    {
        return new HttpRequestException(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} returned a response larger than {1} bytes.",
                providerName,
                MAX_LOOKUP_RESPONSE_BYTES));
    }
}
