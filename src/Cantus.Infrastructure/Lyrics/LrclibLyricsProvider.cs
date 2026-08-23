using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

public class LrclibLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LrclibLyricsProvider> _logger;

    public LrclibLyricsProvider(
        HttpClient httpClient,
        IOptions<LrclibOptions> options,
        ILogger<LrclibLyricsProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var opt = options.Value;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(opt.BaseUrl);
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", opt.UserAgent);
        }
    }

    public virtual async Task<SyncedLyrics?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            // 1. Try exact lookup via /api/get
            var exactResult = await TryGetExactLyricsAsync(track, cancellationToken);
            if (exactResult is not null)
            {
                return MapToDomain(exactResult, track);
            }

            // 2. Fallback to /api/search
            var searchResult = await TrySearchLyricsAsync(track, cancellationToken);
            if (searchResult is not null)
            {
                return MapToDomain(searchResult, track);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to retrieve lyrics from LRCLIB for track {TrackId} ({Artist} - {Title})",
                track.Id, track.Artist, track.Title);
            return null;
        }
    }

    private async Task<LrclibResponseDto?> TryGetExactLyricsAsync(TrackInfo track, CancellationToken ct)
    {
        int durationSec = (int)Math.Round(track.Duration.TotalSeconds);
        string url = $"/api/get?track_name={Uri.EscapeDataString(track.Title)}&artist_name={Uri.EscapeDataString(track.Artist)}";

        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            url += $"&album_name={Uri.EscapeDataString(track.Album)}";
        }

        if (durationSec > 0)
        {
            url += $"&duration={durationSec}";
        }

        using var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LrclibResponseDto>(cancellationToken: ct);
    }

    private async Task<LrclibResponseDto?> TrySearchLyricsAsync(TrackInfo track, CancellationToken ct)
    {
        string query = $"{track.Title} {track.Artist}";
        string url = $"/api/search?q={Uri.EscapeDataString(query)}";

        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var results = await response.Content.ReadFromJsonAsync<List<LrclibResponseDto>>(cancellationToken: ct);
        if (results is null || results.Count == 0)
        {
            return null;
        }

        // Find candidate with synced lyrics and closest duration (within 4 seconds tolerance if duration known)
        int targetDurationSec = (int)Math.Round(track.Duration.TotalSeconds);

        var candidate = results
            .Where(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .OrderBy(r => targetDurationSec > 0 && r.Duration.HasValue ? Math.Abs(r.Duration.Value - targetDurationSec) : 0)
            .FirstOrDefault();

        if (candidate is not null && targetDurationSec > 0 && candidate.Duration.HasValue)
        {
            if (Math.Abs(candidate.Duration.Value - targetDurationSec) > 5)
            {
                // Mismatch too high, don't use inaccurate candidate
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

        var parsed = LrcParser.Parse(dto.SyncedLyrics, track.Id, track.Title, track.Artist, track.Album ?? dto.AlbumName, dto.PlainLyrics);
        return parsed;
    }

    internal sealed class LrclibResponseDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("albumName")]
        public string? AlbumName { get; set; }

        [JsonPropertyName("duration")]
        public int? Duration { get; set; }

        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; set; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }
    }
}
