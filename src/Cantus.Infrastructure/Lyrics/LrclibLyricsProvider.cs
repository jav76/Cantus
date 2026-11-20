using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

public class LrclibLyricsProvider : ILyricsProvider, ILyricsFetchProvider
{
    public string ProviderName => "LRCLIB";

    public bool SupportsWordTiming => false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LrclibLyricsProvider> _logger;

    public LrclibLyricsProvider(
        HttpClient httpClient,
        IOptions<LrclibOptions> options,
        ILogger<LrclibLyricsProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        LrclibOptions opt = options.Value;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(opt.BaseUrl);
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", opt.UserAgent);
        }
    }

    public virtual async Task<SyncedLyrics?> GetLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        LyricsFetchResult result = await FetchLyricsAsync(track, cancellationToken);
        return result.Lyrics;
    }

    /// <summary>
    /// Like <see cref="GetLyricsAsync"/>, but reports whether a null result is
    /// authoritative ("LRCLIB has no lyrics for this track") or transient
    /// ("LRCLIB was unreachable"), so callers can decide whether to
    /// negative-cache it.
    /// </summary>
    public virtual async Task<LyricsFetchResult> FetchLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            // 1. Try exact lookup via /api/get
            LrclibResponseDto? exactResult = await TryGetExactLyricsAsync(track, cancellationToken);
            if (exactResult is not null)
            {
                return LyricsFetchResult.Found(MapToDomain(exactResult, track));
            }

            // 2. Fallback to /api/search
            LrclibResponseDto? searchResult = await TrySearchLyricsAsync(track, cancellationToken);
            if (searchResult is not null)
            {
                return LyricsFetchResult.Found(MapToDomain(searchResult, track));
            }

            // Both endpoints answered without a match: an authoritative miss.
            return LyricsFetchResult.NotFound();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve lyrics from LRCLIB for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return LyricsFetchResult.Unavailable();
        }
    }

    public virtual async Task<IReadOnlyList<LyricsCandidate>> GetCandidatesAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        List<LrclibResponseDto>? results = await SearchAsync(track, cancellationToken);
        if (results is null)
        {
            return [];
        }

        return results
            .Where(r => r.Instrumental
                || !string.IsNullOrWhiteSpace(r.SyncedLyrics)
                || !string.IsNullOrWhiteSpace(r.PlainLyrics))
            .Select(ToCandidate)
            .ToList();
    }

    public virtual async Task<SyncedLyrics?> GetCandidateLyricsAsync(
        TrackInfo track,
        string providerCandidateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!long.TryParse(providerCandidateId, NumberStyles.None, CultureInfo.InvariantCulture, out long recordId))
        {
            return null;
        }

        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"/api/get/{recordId.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        LrclibResponseDto? dto = await response.Content.ReadFromJsonAsync<LrclibResponseDto>(
            JsonOptions,
            cancellationToken: cancellationToken);

        if (dto is null)
        {
            return null;
        }

        bool hasLyrics = dto.Instrumental
            || !string.IsNullOrWhiteSpace(dto.SyncedLyrics)
            || !string.IsNullOrWhiteSpace(dto.PlainLyrics);
        return hasLyrics ? MapToDomain(dto, track) : null;
    }

    private LyricsCandidate ToCandidate(LrclibResponseDto dto)
    {
        IReadOnlyList<string> preview = [];
        bool isSynced = false;
        if (!dto.Instrumental)
        {
            IReadOnlyList<LyricLine> syncedLines = LrcParser.ParseLines(dto.SyncedLyrics);
            isSynced = syncedLines.Count > 0;
            preview = isSynced
                ? LyricsPreview.FromLines(syncedLines)
                : LyricsPreview.FromPlain(dto.PlainLyrics);
        }

        return new LyricsCandidate(
            ProviderName,
            dto.Id.ToString(CultureInfo.InvariantCulture),
            dto.TrackName ?? string.Empty,
            dto.ArtistName ?? string.Empty,
            dto.AlbumName,
            dto.Duration is > 0 ? TimeSpan.FromSeconds(dto.Duration.Value) : TimeSpan.Zero,
            isSynced,
            dto.Instrumental,
            preview);
    }

    private async Task<LrclibResponseDto?> TryGetExactLyricsAsync(TrackInfo track, CancellationToken ct)
    {
        int durationSec = (int)Math.Round(track.Duration.TotalSeconds);
        string trackParam = Uri.EscapeDataString(track.Title);
        string artistParam = Uri.EscapeDataString(track.Artist);
        string url = $"/api/get?track_name={trackParam}&artist_name={artistParam}";

        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            url += $"&album_name={Uri.EscapeDataString(track.Album)}";
        }

        if (durationSec > 0)
        {
            url += $"&duration={durationSec}";
        }

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LrclibResponseDto>(JsonOptions, cancellationToken: ct);
    }

    private async Task<List<LrclibResponseDto>?> SearchAsync(TrackInfo track, CancellationToken ct)
    {
        string query = $"{track.Title} {track.Artist}";
        string url = $"/api/search?q={Uri.EscapeDataString(query)}";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);

        // A failed search (5xx, rate limit) must throw rather than read as
        // "no results": the caller treats an empty search as an authoritative
        // miss and negative-caches it.
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<LrclibResponseDto>>(
            JsonOptions,
            cancellationToken: ct);
    }

    private async Task<LrclibResponseDto?> TrySearchLyricsAsync(TrackInfo track, CancellationToken ct)
    {
        List<LrclibResponseDto>? results = await SearchAsync(track, ct);

        if (results is null || results.Count == 0)
        {
            return null;
        }

        int targetDurationSec = (int)Math.Round(track.Duration.TotalSeconds);

        LrclibResponseDto? candidate = results
            .Where(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .OrderBy(r => targetDurationSec > 0 && r.Duration.HasValue
                ? Math.Abs(r.Duration.Value - targetDurationSec)
                : 0)
            .FirstOrDefault();

        if (candidate is not null && targetDurationSec > 0 && candidate.Duration.HasValue)
        {
            if (Math.Abs(candidate.Duration.Value - targetDurationSec) > 5)
            {
                return null;
            }
        }

        return candidate ?? results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.PlainLyrics) || r.Instrumental);
    }

    private static SyncedLyrics MapToDomain(LrclibResponseDto dto, TrackInfo track)
    {
        if (dto.Instrumental)
        {
            return new SyncedLyrics
            {
                TrackId = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album ?? dto.AlbumName,
                Lines = [],
                IsSynced = false,
                IsInstrumental = true,
                PlainLyrics = null
            };
        }

        SyncedLyrics parsed = LrcParser.Parse(
            dto.SyncedLyrics,
            track.Id,
            track.Title,
            track.Artist,
            track.Album ?? dto.AlbumName,
            dto.PlainLyrics);

        return parsed;
    }

    internal sealed class LrclibResponseDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("albumName")]
        public string? AlbumName { get; set; }

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; set; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }
    }
}
