## SonarQube Quality Report

- **Project**: `Cantus`
- **Quality Gate**: **ERROR**
- **Generated (UTC)**: `2026-11-23T00:54:31Z`
- **Repository**: https://github.com/jav76/Cantus
- **Public Dashboard**: https://jav76.github.io/Cantus/

| Metric | Value |
| :--- | :--- |
| Quality Gate | ERROR |
| Reliability Rating / Issues | D (10) |
| Security Rating / Issues | A (0) |
| Maintainability Rating / Issues | A (1946) |
| Coverage | 74.7% |
| Duplicated Lines | 0.8% |
| Lines of Code (NCLOC) | 27.4k |
| Technical Debt | 4d4h |
| Open Issues (total) | 1956 |
| Open Security Hotspots | 0 |
| SARIF Exported Findings | 0 |

### Quality Gate Conditions

| Metric | Status | Actual | Threshold |
| :--- | :--- | :--- | :--- |
| `new_coverage` | OK | 81.6 | LT 80 |
| `new_duplicated_lines_density` | OK | 0.0 | GT 3 |
| `new_violations` | ERROR | 1 | GT 0 |

### Top Findings (Major / MEDIUM+ and Open Hotspots)

| Kind | Severity | Rule | Location | Message |
| :--- | :--- | :--- | :--- | :--- |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/Models/ArtworkBlur.cs:159` | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/BackgroundServices/ActiveUsersPlaybackMonitor.cs:146` | Refactor this method to reduce its Cognitive Complexity from 69 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Hubs/PlaybackHub.cs:47` | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:911` | Refactor this method to reduce its Cognitive Complexity from 29 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Infrastructure/Lyrics/Lookup/SongLookupService.cs:161` | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Services/SongPlaybackService.cs:56` | Refactor this method to reduce its Cognitive Complexity from 22 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Search/SmithWatermanAligner.cs:19` | Refactor this method to reduce its Cognitive Complexity from 26 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Server/Program.cs` | Refactor this top-level file to reduce its Cognitive Complexity from 27 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/Views/LyricsStageView.xaml.cs:186` | Refactor this method to reduce its Cognitive Complexity from 20 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:280` | Refactor this method to reduce its Cognitive Complexity from 28 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:47` | Refactor this method to reduce its Cognitive Complexity from 52 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Parsers/LrcParser.cs:50` | Refactor this method to reduce its Cognitive Complexity from 19 to the 15 allowed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S3776` | `src/Cantus.Core/Parsers/LrcParser.cs:110` | Refactor this method to reduce its Cognitive Complexity from 39 to the 15 allowed. |
| Issue | MAJOR/HIGH | `csharpsquid:S8949` | `src/Cantus.Infrastructure/Logging/SqliteBatchLogAppender.cs:38` | Pass the 'this._cts.Token' to this method to allow cancellation of the operation, or use 'CancellationToken.None' to opt out explicitly. |
| Issue | CRITICAL/HIGH | `python:S3776` | `scripts/generate_icons.py:119` | Refactor this function to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Server/Endpoints/AuthEndpoints.cs:188` | Method has 11 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:485` | Constructor has 11 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Server/Endpoints/AuthEndpoints.cs:343` | Method has 9 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Server/Endpoints/AuthEndpoints.cs:391` | Method has 8 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Infrastructure/Lyrics/Lookup/SongLookupService.cs:56` | Constructor has 9 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Server/Endpoints/LyricsEndpoints.cs:90` | Method has 8 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Server/Endpoints/DisplayEndpoints.cs:80` | Method has 10 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S107` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:415` | Constructor has 9 parameters, which is greater than the 7 authorized. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S108` | `src/Cantus.Client/Cantus.Client/ViewModels/AmbientBackdrop.cs:114` | Either remove or fill this block of code. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S108` | `src/Cantus.Client/Cantus.Client/ViewModels/SongLookupViewModel.cs:401` | Either remove or fill this block of code. |
