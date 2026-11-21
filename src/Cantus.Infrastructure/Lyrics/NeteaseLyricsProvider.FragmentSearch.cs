using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cantus.Infrastructure.Lyrics.Lookup;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Lyric fragment search through NetEase's lyric search (<c>type=1006</c>),
/// which matches lyric text and returns an excerpt of each song's lyrics. One
/// request per search; no lyric fetch follows and nothing is cached.
/// </summary>
public partial class NeteaseLyricsProvider : ILyricsFragmentSearchProvider
{
    private const int LYRIC_SEARCH_TYPE = 1006;
    private const int FRAGMENT_SEARCH_LIMIT = 10;
    private const int MAX_FRAGMENT_QUERY_CHARS = 100;
    private const int HIGHLIGHT_TAG_REGEX_TIMEOUT_MS = 200;

    // Lyric search wraps the matched words in <b> highlight tags.
    [GeneratedRegex(
        @"</?b\s*>",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: HIGHLIGHT_TAG_REGEX_TIMEOUT_MS)]
    private static partial Regex HighlightTagRegex();

    public virtual async Task<IReadOnlyList<LyricsFragmentHit>> SearchByFragmentAsync(
        LyricsFragmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string fragment = TrimFragment(query.Text);
        if (fragment.Length == 0)
        {
            return [];
        }

        string url = $"/api/search/get?s={Uri.EscapeDataString(fragment)}"
            + $"&type={LYRIC_SEARCH_TYPE}&limit={FRAGMENT_SEARCH_LIMIT}";
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        ProviderRequestOptions.MarkAsLookup(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        NeteaseFragmentSearchResponseDto? search = await ProviderResponseReader
            .ReadJsonAsync<NeteaseFragmentSearchResponseDto>(response, JsonOptions, PROVIDER_NAME, cancellationToken);

        // A throttle or challenge arrives as HTTP 200 with a payload code; the
        // caller reports it to the rate gate as a lookup-only cooldown.
        if (search is null || search.Code != NETEASE_SUCCESS_CODE)
        {
            throw new ProviderPayloadRejectedException(PROVIDER_NAME, search?.Code);
        }

        List<LyricsFragmentHit> hits = (search.Result?.Songs ?? [])
            .Select(ToFragmentHit)
            .ToList();

        _logger.LogDebug("NetEase lyric search returned {HitCount} songs", hits.Count);
        return hits;
    }

    private static string TrimFragment(string? text)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length <= MAX_FRAGMENT_QUERY_CHARS)
        {
            return trimmed;
        }

        int length = char.IsHighSurrogate(trimmed[MAX_FRAGMENT_QUERY_CHARS - 1])
            ? MAX_FRAGMENT_QUERY_CHARS - 1
            : MAX_FRAGMENT_QUERY_CHARS;
        return trimmed[..length].TrimEnd();
    }

    private LyricsFragmentHit ToFragmentHit(NeteaseFragmentSongDto song)
    {
        IReadOnlyList<string> lines = ReadExcerptLines(song.Lyrics);
        LyricsCandidate candidate = ToCandidate(
            song,
            isSynced: false,
            isInstrumental: false,
            LyricsPreview.FromPlain(string.Join('\n', lines)));

        return new LyricsFragmentHit(candidate, string.Join('\n', lines));
    }

    /// <summary>
    /// The excerpt comes back either as <c>{"txt": "...", "range": [...]}</c>
    /// or as an array of highlighted lines; anything else counts as no excerpt.
    /// </summary>
    internal static IReadOnlyList<string> ReadExcerptLines(JsonElement lyrics)
    {
        IEnumerable<string?> raw = lyrics.ValueKind switch
        {
            JsonValueKind.Object when lyrics.TryGetProperty("txt", out JsonElement txt)
                && txt.ValueKind == JsonValueKind.String => txt.GetString()?.Split('\n') ?? [],
            JsonValueKind.Array => lyrics.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()),
            JsonValueKind.String => lyrics.GetString()?.Split('\n') ?? [],
            _ => [],
        };

        return raw
            .OfType<string>()
            .Select(line => HighlightTagRegex().Replace(line, string.Empty).Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    internal sealed class NeteaseFragmentSearchResponseDto
    {
        [JsonPropertyName("code")]
        public int Code { get; set; } = NETEASE_SUCCESS_CODE;

        [JsonPropertyName("result")]
        public NeteaseFragmentSearchResultDto? Result { get; set; }
    }

    internal sealed class NeteaseFragmentSearchResultDto
    {
        [JsonPropertyName("songs")]
        public List<NeteaseFragmentSongDto>? Songs { get; set; }
    }

    internal sealed class NeteaseFragmentSongDto : NeteaseSongDto
    {
        [JsonPropertyName("lyrics")]
        public JsonElement Lyrics { get; set; }
    }
}
