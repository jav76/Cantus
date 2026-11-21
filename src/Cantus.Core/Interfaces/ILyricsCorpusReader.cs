using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

/// <summary>
/// Reads the effective lyrics the song lookup searches. Any override shadows the
/// cache row of its track; a "no lyrics" or instrumental override, a negative or
/// instrumental cache row and an expired cache row are left out. Return values are
/// not trace-logged because they carry full lyric text.
/// </summary>
[TraceLog(true, false)]
public interface ILyricsCorpusReader
{
    Task<IReadOnlyList<LyricsCorpusEntry>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The effective lyrics of the given tracks. A requested id missing from the
    /// result has no searchable lyrics.
    /// </summary>
    Task<IReadOnlyList<LyricsCorpusEntry>> ReadAsync(
        IReadOnlyCollection<string> trackIds,
        CancellationToken cancellationToken = default);
}
