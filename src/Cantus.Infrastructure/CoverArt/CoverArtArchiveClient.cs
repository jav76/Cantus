using System.Globalization;
using System.Net;

namespace Cantus.Infrastructure.CoverArt;

/// <summary>Asks the Cover Art Archive whether a release group has a front cover.</summary>
public sealed class CoverArtArchiveClient
{
    public const string PROVIDER_NAME = "Cover Art Archive";
    public const int THUMBNAIL_SIZE = 500;

    private const string IMAGE_MEDIA_TYPE_PREFIX = "image/";

    private readonly HttpClient _httpClient;

    public CoverArtArchiveClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// The archive's own URL for the release group's front cover thumbnail, or null when it has
    /// none. The request follows the archive's redirects to the image itself, because a
    /// thumbnail can be missing even when the archive lists a cover. Throws when the archive
    /// cannot be reached or answers with anything but an image or "not found".
    /// </summary>
    public async Task<string?> FindFrontCoverAsync(Guid releaseGroupId, CancellationToken cancellationToken = default)
    {
        string path = string.Create(
            CultureInfo.InvariantCulture,
            $"/release-group/{releaseGroupId:D}/front-{THUMBNAIL_SIZE}");

        using HttpRequestMessage request = new(HttpMethod.Head, path);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith(IMAGE_MEDIA_TYPE_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException($"{PROVIDER_NAME} answered with '{mediaType}' instead of an image.");
        }

        return new Uri(_httpClient.BaseAddress!, path).AbsoluteUri;
    }
}
