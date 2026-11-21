using Cantus.Core.Models;
using Cantus.Core.Parsers;
using Cantus.Infrastructure.Lyrics.Lookup;
using Microsoft.Extensions.Logging;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Lyric fragment search by title guess. LRCLIB search matches only title,
/// artist and album, so each guess is sent as <c>track_name</c> and the
/// returned lyrics are left for the caller to score against the fragment.
/// Nothing is cached.
/// </summary>
public partial class LrclibLyricsProvider : ILyricsFragmentSearchProvider
{
    /// <summary>Most title guesses sent to LRCLIB for one fragment search.</summary>
    public const int MAX_TITLE_GUESSES = 2;

    public virtual async Task<IReadOnlyList<LyricsFragmentHit>> SearchByFragmentAsync(
        LyricsFragmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<string> guesses = query.TitleGuesses
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MAX_TITLE_GUESSES)
            .ToList();

        List<LyricsFragmentHit> hits = new();
        HashSet<long> seenRecordIds = new();
        int requestsSent = 0;

        foreach (string guess in guesses)
        {
            List<LrclibResponseDto>? records;
            try
            {
                requestsSent++;
                records = await SearchByTrackNameAsync(guess, cancellationToken);
            }
            catch (Exception ex) when (hits.Count > 0)
            {
                // Hits from an earlier guess still answer the search, also after a timeout.
                // The caller decides whether a cancelled search is still wanted.
                _logger.LogWarning(
                    ex,
                    "LRCLIB title-guess request {RequestNumber} failed; keeping {HitCount} earlier hits",
                    requestsSent,
                    hits.Count);
                break;
            }

            foreach (LrclibResponseDto record in records ?? [])
            {
                string searchableText = ToSearchableText(record);
                if (searchableText.Length > 0 && seenRecordIds.Add(record.Id))
                {
                    hits.Add(new LyricsFragmentHit(ToCandidate(record), searchableText));
                }
            }
        }

        _logger.LogDebug(
            "LRCLIB title-guess search returned {HitCount} records from {RequestCount} requests",
            hits.Count,
            requestsSent);
        return hits;
    }

    private async Task<List<LrclibResponseDto>?> SearchByTrackNameAsync(string trackName, CancellationToken ct)
    {
        string url = $"/api/search?track_name={Uri.EscapeDataString(trackName)}";
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        ProviderRequestOptions.MarkAsLookup(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        return await ProviderResponseReader.ReadJsonAsync<List<LrclibResponseDto>>(
            response,
            JsonOptions,
            PROVIDER_NAME,
            ct);
    }

    private static string ToSearchableText(LrclibResponseDto record)
    {
        if (record.Instrumental)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(record.PlainLyrics))
        {
            return record.PlainLyrics.Trim();
        }

        IReadOnlyList<LyricLine> lines = LrcParser.ParseLines(record.SyncedLyrics);
        return string.Join('\n', lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
    }
}
