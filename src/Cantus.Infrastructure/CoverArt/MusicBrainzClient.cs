using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;

namespace Cantus.Infrastructure.CoverArt;

/// <summary>Searches MusicBrainz for the recordings a track could be.</summary>
public sealed class MusicBrainzClient
{
    public const string PROVIDER_NAME = "MusicBrainz";

    /// <summary>MusicBrainz allows one request per second from an address; this leaves a margin.</summary>
    public static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1100);

    // For a well-known song the studio recording ranks below dozens of compilation-only
    // recordings with the same score, so a short page often misses it. 100 is the API's maximum.
    private const int SEARCH_LIMIT = 100;
    private const int MAX_RESPONSE_BYTES = 4 * 1024 * 1024;
    private const string LUCENE_SPECIAL_CHARACTERS = "+-&|!(){}[]^\"~*?:\\/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;

    public MusicBrainzClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Recordings whose title and artist share words with the track's and whose length is
    /// within the matcher's tolerance. Throws when MusicBrainz cannot be reached or refuses.
    /// </summary>
    public async Task<IReadOnlyList<MusicBrainzRecording>> SearchRecordingsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        string url = string.Create(
            CultureInfo.InvariantCulture,
            $"/ws/2/recording?query={Uri.EscapeDataString(BuildQuery(track))}&limit={SEARCH_LIMIT}&fmt=json");

        using HttpResponseMessage response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        SearchResponseDto? dto = await ProviderResponseReader.ReadJsonAsync<SearchResponseDto>(
            response,
            JsonOptions,
            PROVIDER_NAME,
            cancellationToken,
            MAX_RESPONSE_BYTES);

        return dto?.Recordings is { } recordings
            ? recordings.Select(ToRecording).ToList()
            : [];
    }

    /// <summary>
    /// Title and artist are sent as loose terms, not phrases: the titles players report carry
    /// qualifiers ("Remastered 2009") that the catalogue's titles lack, and the matcher makes the
    /// real decision. The album only raises the rank of recordings that appear on it.
    /// </summary>
    internal static string BuildQuery(TrackInfo track)
    {
        double toleranceMs = TimeSpan.FromSeconds(MusicBrainzReleaseMatcher.DURATION_TOLERANCE_SECONDS).TotalMilliseconds;
        long shortestMs = (long)Math.Max(0, track.Duration.TotalMilliseconds - toleranceMs);
        long longestMs = (long)(track.Duration.TotalMilliseconds + toleranceMs);

        StringBuilder query = new();
        query.Append(CultureInfo.InvariantCulture, $"recording:({EscapeTerms(track.Title)})");
        query.Append(CultureInfo.InvariantCulture, $" AND artistname:({EscapeTerms(track.Artist)})");
        query.Append(CultureInfo.InvariantCulture, $" AND dur:[{shortestMs} TO {longestMs}]");
        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            query.Append(CultureInfo.InvariantCulture, $" release:({EscapeTerms(track.Album)})");
        }

        return query.ToString();
    }

    /// <summary>
    /// Escapes Lucene's syntax characters and lower-cases the text, because AND, OR and NOT in
    /// capitals are operators even inside a group.
    /// </summary>
    private static string EscapeTerms(string text)
    {
        StringBuilder escaped = new(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            if (LUCENE_SPECIAL_CHARACTERS.Contains(c))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    private static MusicBrainzRecording ToRecording(RecordingDto dto)
    {
        List<string> artists = new();
        foreach (ArtistCreditDto credit in dto.ArtistCredit ?? [])
        {
            AddName(artists, credit.Name);
            AddName(artists, credit.Artist?.Name);
        }

        List<MusicBrainzRelease> releases = new();
        foreach (ReleaseDto release in dto.Releases ?? [])
        {
            // The id ends up in a URL sent to viewers, so anything that is not a UUID is dropped.
            if (Guid.TryParseExact(release.ReleaseGroup?.Id, "D", out Guid releaseGroupId))
            {
                releases.Add(new MusicBrainzRelease(
                    releaseGroupId,
                    release.Title ?? string.Empty,
                    release.Status,
                    release.ReleaseGroup!.PrimaryType,
                    release.ReleaseGroup.SecondaryTypes ?? [],
                    release.Date));
            }
        }

        return new MusicBrainzRecording(
            dto.Title ?? string.Empty,
            artists,
            dto.Length is > 0 ? TimeSpan.FromMilliseconds(dto.Length.Value) : null,
            releases);
    }

    private static void AddName(List<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
        {
            names.Add(name);
        }
    }

    internal sealed class SearchResponseDto
    {
        [JsonPropertyName("recordings")]
        public List<RecordingDto>? Recordings { get; set; }
    }

    internal sealed class RecordingDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("length")]
        public long? Length { get; set; }

        [JsonPropertyName("artist-credit")]
        public List<ArtistCreditDto>? ArtistCredit { get; set; }

        [JsonPropertyName("releases")]
        public List<ReleaseDto>? Releases { get; set; }
    }

    internal sealed class ArtistCreditDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("artist")]
        public ArtistDto? Artist { get; set; }
    }

    internal sealed class ArtistDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    internal sealed class ReleaseDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("release-group")]
        public ReleaseGroupDto? ReleaseGroup { get; set; }
    }

    internal sealed class ReleaseGroupDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("primary-type")]
        public string? PrimaryType { get; set; }

        [JsonPropertyName("secondary-types")]
        public List<string>? SecondaryTypes { get; set; }
    }
}
