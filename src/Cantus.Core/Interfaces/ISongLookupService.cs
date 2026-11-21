using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

/// <summary>
/// Finds songs from a remembered lyric fragment: the server's own lyrics first,
/// then the lyrics providers within rate limits. Never writes the lyrics cache.
/// Neither parameters nor results are trace-logged, because they carry the
/// fragment and matched lyric lines.
/// </summary>
[TraceLog(false, false)]
public interface ISongLookupService
{
    /// <summary>
    /// Ranks guesses for <paramref name="fragment"/>. Provider trouble never
    /// throws; it is reported per provider in the result.
    /// </summary>
    /// <param name="fragment">The remembered lyric fragment as typed.</param>
    /// <param name="scope">Where to search.</param>
    /// <param name="spotifyUserId">The caller's Spotify user id, which keys the online lookup budget.</param>
    /// <param name="cancellationToken">Cancels the whole lookup.</param>
    /// <exception cref="ArgumentException">The fragment is empty, too short or too long.</exception>
    Task<SongLookupResult> LookupAsync(
        string fragment,
        SongLookupScope scope,
        string spotifyUserId,
        CancellationToken cancellationToken = default);
}
