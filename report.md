## SonarQube Quality Report

- **Project**: `Cantus`
- **Quality Gate**: **OK**
- **Generated (UTC)**: `2026-11-22T21:44:03Z`
- **Repository**: https://github.com/jav76/Cantus
- **Public Dashboard**: https://jav76.github.io/Cantus/

| Metric | Value |
| :--- | :--- |
| Quality Gate | OK |
| Reliability Rating / Issues | E (14) |
| Security Rating / Issues | B (5) |
| Maintainability Rating / Issues | A (1982) |
| Coverage | 72.8% |
| Duplicated Lines | 0.8% |
| Lines of Code (NCLOC) | 27.8k |
| Technical Debt | 5d47min |
| Open Issues (total) | 2001 |
| Open Security Hotspots | 0 |
| SARIF Exported Findings | 5 |

### Top Findings (Major / MEDIUM+ and Open Hotspots)

| Kind | Severity | Rule | Location | Message |
| :--- | :--- | :--- | :--- | :--- |
| Issue | BLOCKER/BLOCKER | `csharpsquid:S2699` | `tests/Cantus.Client.Tests/ViewModels/LyricsViewModelTests.cs:492` | Add at least one assertion to this test case. |
| Issue | BLOCKER/BLOCKER | `csharpsquid:S2930` | `src/Cantus.Infrastructure/Logging/SqliteBatchLogAppender.cs:35` | Dispose '_cts' when it is no longer needed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S2551` | `src/Cantus.Client/Cantus.Client/Services/ClientLoggingManager.cs:123` | Lock on a dedicated object instance instead. |
| Issue | CRITICAL/HIGH | `csharpsquid:S2696` | `src/Cantus.Client/Cantus.Client/Services/ClientLoggingManager.cs:114` | Make the enclosing instance property 'static' or remove this set on the 'static' field. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Endpoints/AuthEndpoints.cs:196` | Refactor this method to reduce its Cognitive Complexity from 17 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/Models/ArtworkBlur.cs:159` | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/BackgroundServices/ActiveUsersPlaybackMonitor.cs:146` | Refactor this method to reduce its Cognitive Complexity from 69 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Hubs/PlaybackHub.cs:47` | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/Models/SyllableFillTimeline.cs:54` | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:842` | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:900` | Refactor this method to reduce its Cognitive Complexity from 29 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Infrastructure/Lyrics/Lookup/SongLookupService.cs:161` | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Services/SongPlaybackService.cs:56` | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Search/SmithWatermanAligner.cs:19` | Refactor this method to reduce its Cognitive Complexity from 26 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Search/TitleGuessGenerator.cs:30` | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Infrastructure/Lyrics/CachedLyricsService.cs:230` | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:1046` | Refactor this method to reduce its Cognitive Complexity from 16 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Program.cs` | Refactor this top-level file to reduce its Cognitive Complexity from 26 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/Views/LyricsStageView.xaml.cs:186` | Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:280` | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:47` | Refactor this method to reduce its Cognitive Complexity from 52 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Infrastructure/Logging/SqliteBatchLogAppender.cs:63` | Refactor this method to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Parsers/LrcParser.cs:50` | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Parsers/LrcParser.cs:110` | Refactor this method to reduce its Cognitive Complexity from 39 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S4487` | `src/Cantus.Server/Services/HostUrlResolver.cs:11` | Remove this unread private field '_spotifyOptions' or refactor the code to use its value. |
