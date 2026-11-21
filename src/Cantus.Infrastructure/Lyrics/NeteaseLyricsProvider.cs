using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cantus.Core.Models;
using Cantus.Core.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Fallback lyrics source backed by NetEase Cloud Music's unofficial web API
/// (no key required; the same endpoints used by open-source fetchers such as
/// syncedlyrics). Synced-only by design: a result without timestamped lines is
/// reported as an authoritative miss rather than served as plain text, since
/// LRCLIB earlier in the chain already covers plain lyrics.
/// </summary>
public partial class NeteaseLyricsProvider : ILyricsFetchProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // NetEase signals API errors, rate limits, and anti-scraping challenges
    // via the payload's root "code" (e.g. -460 for a captcha challenge) while
    // still returning HTTP 200, so payload codes must be checked explicitly.
    private const int NETEASE_SUCCESS_CODE = 200;

    // Substring artist matching below this length produces false positives
    // ("Steve".Contains("Eve")); shorter names must match a full token.
    private const int MIN_ARTIST_SUBSTRING_MATCH_LENGTH = 3;

    private const int CREDIT_LINE_REGEX_TIMEOUT_MS = 200;

    // Each candidate costs one extra request to an unofficial, rate-limited
    // API, so a candidate listing confirms only the first few search hits.
    private const int MAX_CANDIDATE_LYRIC_LOOKUPS = 5;

    private static readonly char[] ArtistDelimiters = [',', '/', '&', ';'];

    // NetEase prepends credit lines (lyricist/composer/arranger/etc.) to the
    // LRC body; they are metadata, not lyrics, and would render as the first
    // "sung" lines. Matches timestamped lines whose text starts with common
    // CJK credit labels (full or single-character abbreviations, optionally
    // composite like 词/曲) or English ones, followed by a colon.
    [GeneratedRegex(
        @"^\[\d{2,}:\d{2}(?:[.:]\d{1,3})?\]\s*(?:(?:作?词|作?曲|词曲|编曲|制作人?|监制|混音|母带|录音|出品|发行|和声|吉他|贝斯|键盘|弦乐|鼓)(?:\s*[/／]\s*(?:作?词|作?曲))?|(?:Lyrics|Music|Written|Produced)(?:\s+by)?)\s*[:：]",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: CREDIT_LINE_REGEX_TIMEOUT_MS)]
    private static partial Regex CreditLineRegex();

    private readonly HttpClient _httpClient;
    private readonly NeteaseOptions _options;
    private readonly ILogger<NeteaseLyricsProvider> _logger;

    public NeteaseLyricsProvider(
        HttpClient httpClient,
        IOptions<NeteaseOptions> options,
        ILogger<NeteaseLyricsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        }
    }

    public const string PROVIDER_NAME = "NetEase";

    public string ProviderName => PROVIDER_NAME;

    public virtual async Task<LyricsFetchResult> FetchLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            NeteaseSearchResponseDto? search = await SearchAsync(track, cancellationToken);
            if (search is null || search.Code != NETEASE_SUCCESS_CODE)
            {
                LogPayloadError("search", search?.Code, track);
                return LyricsFetchResult.Unavailable();
            }

            long? songId = SelectBestMatch(search, track);
            if (songId is null)
            {
                return LyricsFetchResult.NotFound();
            }

            NeteaseLyricResponseDto? lyric = await GetLyricAsync(songId.Value, cancellationToken);
            if (lyric is null || lyric.Code != NETEASE_SUCCESS_CODE)
            {
                LogPayloadError("lyric", lyric?.Code, track);
                return LyricsFetchResult.Unavailable();
            }

            return MapLyric(lyric, track, songId.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as a cancellation that the caller
            // did not request: a transient failure, not an authoritative miss.
            _logger.LogWarning(
                "NetEase lyrics request timed out for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return LyricsFetchResult.Unavailable();
        }
        catch (ProviderRateLimitedException ex)
        {
            _logger.LogDebug(
                "Skipped NetEase lyrics for track {TrackId}: requests are paused for {RetryAfterSeconds} s",
                track.Id,
                ex.RetryAfter.TotalSeconds);
            return LyricsFetchResult.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to retrieve lyrics from NetEase for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return LyricsFetchResult.Unavailable();
        }
    }

    /// <summary>
    /// Search results by a matching artist, each confirmed with a lyric
    /// lookup. Duration does not filter here (the owner is correcting a match,
    /// so a live or alternate cut is a valid pick). Like
    /// <see cref="FetchLyricsAsync"/>, only synced lyrics or an instrumental
    /// marker are offered. A failed lookup drops only that song; the call
    /// throws when the search fails or failures leave no candidates.
    /// </summary>
    public virtual async Task<IReadOnlyList<LyricsCandidate>> GetCandidatesAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        NeteaseSearchResponseDto? search = await SearchAsync(track, cancellationToken);
        if (search is null || search.Code != NETEASE_SUCCESS_CODE)
        {
            throw new HttpRequestException($"NetEase search returned payload code {search?.Code}.");
        }

        if (search.Result?.Songs is not { Count: > 0 } songs)
        {
            return [];
        }

        List<NeteaseSongDto> matches = songs
            .Where(s => ArtistsMatch(s, track))
            .Take(MAX_CANDIDATE_LYRIC_LOOKUPS)
            .ToList();

        Task<LyricsCandidate?>[] lookups = matches
            .Select(s => GetCandidateAsync(s, cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(lookups);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Each lookup's outcome is inspected below, so one failed song
            // does not hide the songs whose lyrics were found.
        }

        List<LyricsCandidate> candidates = new();
        Exception? firstFailure = null;
        for (int i = 0; i < lookups.Length; i++)
        {
            Task<LyricsCandidate?> lookup = lookups[i];
            if (lookup.IsCompletedSuccessfully)
            {
                if (lookup.Result is LyricsCandidate candidate)
                {
                    candidates.Add(candidate);
                }

                continue;
            }

            Exception failure = lookup.Exception?.GetBaseException() ?? new TaskCanceledException(lookup);
            firstFailure ??= failure;
            _logger.LogWarning(
                failure,
                "NetEase lyric lookup failed for song {SongId} while listing candidates for track {TrackId}",
                matches[i].Id,
                track.Id);
        }

        // With nothing found, an empty list would claim NetEase has no
        // candidates when it could not answer, so the failure is surfaced.
        if (firstFailure is not null && candidates.Count == 0)
        {
            ExceptionDispatchInfo.Throw(firstFailure);
        }

        return candidates;
    }

    public virtual async Task<SyncedLyrics?> GetCandidateLyricsAsync(
        TrackInfo track,
        string providerCandidateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!long.TryParse(providerCandidateId, NumberStyles.None, CultureInfo.InvariantCulture, out long songId))
        {
            return null;
        }

        NeteaseLyricResponseDto? lyric = await GetLyricAsync(songId, cancellationToken);
        if (lyric is null || lyric.Code != NETEASE_SUCCESS_CODE)
        {
            throw new HttpRequestException($"NetEase lyric lookup returned payload code {lyric?.Code}.");
        }

        return MapLyric(lyric, track, songId).Lyrics;
    }

    private async Task<LyricsCandidate?> GetCandidateAsync(NeteaseSongDto song, CancellationToken ct)
    {
        NeteaseLyricResponseDto? lyric = await GetLyricAsync(song.Id, ct);
        if (lyric is null || lyric.Code != NETEASE_SUCCESS_CODE)
        {
            throw new HttpRequestException($"NetEase lyric lookup returned payload code {lyric?.Code}.");
        }

        if (lyric.NoLyric)
        {
            return ToCandidate(song, isSynced: false, isInstrumental: true, preview: []);
        }

        string? lrcText = lyric.Lrc?.Lyric;
        if (lyric.Uncollected || string.IsNullOrWhiteSpace(lrcText))
        {
            return null;
        }

        IReadOnlyList<LyricLine> lines = LrcParser.ParseLines(StripCreditLines(lrcText));
        if (lines.Count == 0)
        {
            return null;
        }

        return ToCandidate(song, isSynced: true, isInstrumental: false, LyricsPreview.FromLines(lines));
    }

    private LyricsCandidate ToCandidate(
        NeteaseSongDto song,
        bool isSynced,
        bool isInstrumental,
        IReadOnlyList<string> preview)
    {
        IEnumerable<string> artistNames = (song.Artists ?? [])
            .Select(a => a.Name)
            .OfType<string>()
            .Where(n => !string.IsNullOrWhiteSpace(n));

        return new LyricsCandidate(
            ProviderName,
            song.Id.ToString(CultureInfo.InvariantCulture),
            song.Name ?? string.Empty,
            string.Join(", ", artistNames),
            song.Album?.Name,
            TimeSpan.FromMilliseconds(song.Duration),
            isSynced,
            isInstrumental,
            preview);
    }

    private void LogPayloadError(string endpoint, int? code, TrackInfo track)
    {
        _logger.LogWarning(
            "NetEase {Endpoint} returned payload code {Code} for track {TrackId} ({Artist} - {Title}); treating as unavailable.",
            endpoint,
            code,
            track.Id,
            track.Artist,
            track.Title);
    }

    private async Task<NeteaseSearchResponseDto?> SearchAsync(TrackInfo track, CancellationToken ct)
    {
        string query = Uri.EscapeDataString($"{track.Title} {track.Artist}");
        string url = $"/api/search/get?s={query}&type=1&limit={_options.SearchLimit}";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<NeteaseSearchResponseDto>(JsonOptions, cancellationToken: ct);
    }

    private long? SelectBestMatch(NeteaseSearchResponseDto search, TrackInfo track)
    {
        if (search.Result?.Songs is not { Count: > 0 } songs)
        {
            return null;
        }

        int targetDurationSec = (int)Math.Round(track.Duration.TotalSeconds);

        NeteaseSongDto? match = songs
            .Where(s => ArtistsMatch(s, track))
            .Where(s => targetDurationSec <= 0 || DurationWithinTolerance(s, targetDurationSec))
            .OrderBy(s => targetDurationSec > 0
                ? Math.Abs((s.Duration / 1000) - targetDurationSec)
                : 0)
            .FirstOrDefault();

        return match?.Id;
    }

    private bool DurationWithinTolerance(NeteaseSongDto song, int targetDurationSec)
    {
        return Math.Abs((song.Duration / 1000) - targetDurationSec) <= _options.DurationToleranceSeconds;
    }

    private static bool ArtistsMatch(NeteaseSongDto song, TrackInfo track)
    {
        if (song.Artists is null || song.Artists.Count == 0)
        {
            return false;
        }

        // Spotify joins artists into one string ("Kendrick Lamar, SZA") while
        // NetEase returns individual artist objects; tokenize before comparing
        // so short names cannot substring-match unrelated artists.
        string[] trackArtists = track.Artist
            .Split(ArtistDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return song.Artists.Any(a =>
            !string.IsNullOrWhiteSpace(a.Name) &&
            trackArtists.Any(ta =>
                string.Equals(ta, a.Name, StringComparison.OrdinalIgnoreCase) ||
                (a.Name.Length >= MIN_ARTIST_SUBSTRING_MATCH_LENGTH && ContainsWholeWord(ta, a.Name))));
    }

    /// <summary>
    /// Containment with word boundaries: "The Chemical Brothers" contains
    /// "Chemical Brothers", but "Steve" does not contain "Eve" - a plain
    /// substring check matches mid-word and picks wrong songs.
    /// </summary>
    private static bool ContainsWholeWord(string haystack, string needle)
    {
        int index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool startsAtBoundary = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            int end = index + needle.Length;
            bool endsAtBoundary = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (startsAtBoundary && endsAtBoundary)
            {
                return true;
            }

            index = haystack.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<NeteaseLyricResponseDto?> GetLyricAsync(long songId, CancellationToken ct)
    {
        string url = $"/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<NeteaseLyricResponseDto>(JsonOptions, cancellationToken: ct);
    }

    private LyricsFetchResult MapLyric(NeteaseLyricResponseDto lyric, TrackInfo track, long songId)
    {
        if (lyric.NoLyric)
        {
            return LyricsFetchResult.Found(new SyncedLyrics
            {
                TrackId = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                Lines = [],
                IsSynced = false,
                IsInstrumental = true,
                PlainLyrics = null
            });
        }

        string? lrcText = lyric.Lrc?.Lyric;
        if (lyric.Uncollected || string.IsNullOrWhiteSpace(lrcText))
        {
            return LyricsFetchResult.NotFound();
        }

        string cleaned = StripCreditLines(lrcText);
        SyncedLyrics parsed = LrcParser.Parse(
            cleaned,
            track.Id,
            track.Title,
            track.Artist,
            track.Album,
            plainLyrics: null);

        if (parsed.Lines is not { Count: > 0 })
        {
            return LyricsFetchResult.NotFound();
        }

        _logger.LogInformation(
            "NetEase lyrics found for track {TrackId} ({Artist} - {Title}) via song id {SongId}",
            track.Id,
            track.Artist,
            track.Title,
            songId);

        return LyricsFetchResult.Found(parsed);
    }

    internal static string StripCreditLines(string lrcText)
    {
        IEnumerable<string> kept = lrcText
            .Split('\n')
            .Where(line => !CreditLineRegex().IsMatch(line.TrimEnd('\r')));

        return string.Join('\n', kept);
    }

    internal sealed class NeteaseSearchResponseDto
    {
        [JsonPropertyName("code")]
        public int Code { get; set; } = NETEASE_SUCCESS_CODE;

        [JsonPropertyName("result")]
        public NeteaseSearchResultDto? Result { get; set; }
    }

    internal sealed class NeteaseSearchResultDto
    {
        [JsonPropertyName("songs")]
        public List<NeteaseSongDto>? Songs { get; set; }
    }

    internal class NeteaseSongDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Duration in milliseconds.</summary>
        [JsonPropertyName("duration")]
        public long Duration { get; set; }

        [JsonPropertyName("artists")]
        public List<NeteaseArtistDto>? Artists { get; set; }

        [JsonPropertyName("album")]
        public NeteaseAlbumDto? Album { get; set; }
    }

    internal sealed class NeteaseAlbumDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    internal sealed class NeteaseArtistDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    internal sealed class NeteaseLyricResponseDto
    {
        [JsonPropertyName("code")]
        public int Code { get; set; } = NETEASE_SUCCESS_CODE;

        [JsonPropertyName("nolyric")]
        public bool NoLyric { get; set; }

        [JsonPropertyName("uncollected")]
        public bool Uncollected { get; set; }

        [JsonPropertyName("lrc")]
        public NeteaseLrcDto? Lrc { get; set; }
    }

    internal sealed class NeteaseLrcDto
    {
        [JsonPropertyName("lyric")]
        public string? Lyric { get; set; }
    }
}
