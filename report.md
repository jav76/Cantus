## SonarQube Quality Report

- **Project**: `Cantus`
- **Quality Gate**: **OK**
- **Generated (UTC)**: `2026-11-24T01:52:52Z`
- **Repository**: https://github.com/jav76/Cantus
- **Public Dashboard**: https://jav76.github.io/Cantus/

| Metric | Value |
| :--- | :--- |
| Quality Gate | OK |
| Reliability Rating / Issues | D (10) |
| Security Rating / Issues | A (0) |
| Maintainability Rating / Issues | A (1772) |
| Coverage | 74.7% |
| Duplicated Lines | 0.8% |
| Lines of Code (NCLOC) | 27.4k |
| Technical Debt | 3h4min |
| Open Issues (total) | 1782 |
| Open Security Hotspots | 0 |
| SARIF Exported Findings | 0 |

### Quality Gate Conditions

| Metric | Status | Actual | Threshold |
| :--- | :--- | :--- | :--- |
| `new_coverage` | OK | 81.9 | LT 50 |
| `new_duplicated_lines_density` | OK | 0.0 | GT 5 |
| `new_software_quality_reliability_rating` | OK | 1 | GT 2 |
| `new_software_quality_security_rating` | OK | 1 | GT 2 |

### Top Findings (Major / MEDIUM+ and Open Hotspots)

| Kind | Severity | Rule | Location | Message |
| :--- | :--- | :--- | :--- | :--- |
| Issue | MAJOR/HIGH | `csharpsquid:S8949` | `src/Cantus.Infrastructure/Logging/SqliteBatchLogAppender.cs:38` | Pass the 'this._cts.Token' to this method to allow cancellation of the operation, or use 'CancellationToken.None' to opt out explicitly. |
| Issue | CRITICAL/HIGH | `python:S3776` | `scripts/generate_icons.py:119` | Refactor this function to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S1244` | `src/Cantus.Client/Cantus.Client/Models/ColorExtractionHelper.cs:190` | Do not check floating point equality with exact values, use a range instead. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S1244` | `src/Cantus.Client/Cantus.Client/Models/ColorExtractionHelper.cs:201` | Do not check floating point equality with exact values, use a range instead. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S1244` | `src/Cantus.Client/Cantus.Client/Models/ColorExtractionHelper.cs:205` | Do not check floating point equality with exact values, use a range instead. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Server/BackgroundServices/ActiveUsersPlaybackMonitor.cs:146` | Refactor this method to reduce its Cognitive Complexity from 69 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:918` | Refactor this method to reduce its Cognitive Complexity from 29 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Infrastructure/Lyrics/Lookup/SongLookupService.cs:161` | Refactor this method to reduce its Cognitive Complexity from 28 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Core/Search/SmithWatermanAligner.cs:19` | Refactor this method to reduce its Cognitive Complexity from 26 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Server/Program.cs` | Refactor this top-level file to reduce its Cognitive Complexity from 27 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:280` | Refactor this method to reduce its Cognitive Complexity from 28 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:47` | Refactor this method to reduce its Cognitive Complexity from 52 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Core/Parsers/LrcParser.cs:110` | Refactor this method to reduce its Cognitive Complexity from 39 to the 25 allowed. |
| Issue | MAJOR/LOW | `csharpsquid:S6966` | `src/Cantus.Infrastructure/Lyrics/ProviderResponseReader.cs:39` | Await WriteAsync instead. |
| Issue | MAJOR/LOW | `csharpsquid:S6966` | `src/Cantus.Client/Cantus.Client/Services/SignalRPlaybackClient.cs:892` | Await DisposeAsync instead. |
| Issue | MAJOR/LOW | `csharpsquid:S6966` | `src/Cantus.Client/Cantus.Client/Services/SignalRPlaybackClient.cs:893` | Await DisposeAsync instead. |
| Issue | MAJOR/LOW | `csharpsquid:S6966` | `src/Cantus.Server/Program.cs:225` | Await MigrateAsync instead. |
| Issue | MAJOR/LOW | `csharpsquid:S6966` | `src/Cantus.Server/Program.cs:276` | Await RunAsync instead. |
| Issue | MAJOR/MEDIUM | `css:S7924` | `docs/stylesheets/extra.css:156` | Text does not meet the minimal contrast requirement with its background. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `docs/stylesheets/extra.css:71` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `docs/stylesheets/extra.css:81` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `docs/stylesheets/extra.css:92` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `src/Cantus.Client/Cantus.Client/WasmScripts/index.html:180` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9440` | `docs/stylesheets/extra.css:72` | Respect reduced-motion preferences here to avoid triggering motion sensitivity. |
| Issue | MAJOR/MEDIUM | `css:S9440` | `docs/stylesheets/extra.css:113` | Respect reduced-motion preferences here to avoid triggering motion sensitivity. |
