## SonarQube Quality Report

- **Project**: `Cantus`
- **Quality Gate**: **ERROR**
- **Generated (UTC)**: `2026-11-25T22:13:55Z`
- **Repository**: https://github.com/jav76/Cantus
- **Public Dashboard**: https://jav76.github.io/Cantus/

| Metric | Value |
| :--- | :--- |
| Quality Gate | ERROR |
| Reliability Rating / Issues | E (2) |
| Security Rating / Issues | A (0) |
| Maintainability Rating / Issues | A (2562) |
| Coverage | 74.6% |
| Duplicated Lines | 0.5% |
| Lines of Code (NCLOC) | 36k |
| Technical Debt | 3h39min |
| Open Issues (total) | 2564 |
| Open Security Hotspots | 0 |
| SARIF Exported Findings | 0 |

### Quality Gate Conditions

| Metric | Status | Actual | Threshold |
| :--- | :--- | :--- | :--- |
| `new_coverage` | OK | 73.1 | LT 50 |
| `new_duplicated_lines_density` | OK | 0.0 | GT 5 |
| `new_software_quality_reliability_rating` | ERROR | 5 | GT 2 |
| `new_software_quality_security_rating` | OK | 1 | GT 2 |

### Top Findings (Major / MEDIUM+ and Open Hotspots)

| Kind | Severity | Rule | Location | Message |
| :--- | :--- | :--- | :--- | :--- |
| Issue | BLOCKER/BLOCKER | `csharpsquid:S2699` | `tests/Cantus.Server.Tests/Services/CoverArtBackfillTests.cs:123` | Add at least one assertion to this test case. |
| Issue | BLOCKER/BLOCKER | `csharpsquid:S2930` | `src/Cantus.Server/Services/CoverArtBackfill.cs:56` | Dispose '_stopping' when it is no longer needed. |
| Issue | CRITICAL/HIGH | `csharpsquid:S1994` | `src/Cantus.Server/Services/CoverArtBackfill.cs:157` | This loop's stop condition tests 'this' and 'track' but the incrementer updates 'attempt'. |
| Issue | MAJOR/HIGH | `csharpsquid:S8949` | `src/Cantus.Server/Services/CoverArtBackfill.cs:103` | Pass the 'this._stopping.Token' to this method to allow cancellation of the operation, or use 'CancellationToken.None' to opt out explicitly. |
| Issue | CRITICAL/HIGH | `python:S3776` | `scripts/generate_icons.py:119` | Refactor this function to reduce its Cognitive Complexity from 18 to the 15 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Server/BackgroundServices/ActiveUsersPlaybackMonitor.cs:146` | Refactor this method to reduce its Cognitive Complexity from 73 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs:979` | Refactor this method to reduce its Cognitive Complexity from 29 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Infrastructure/Lyrics/Lookup/SongLookupService.cs:161` | Refactor this method to reduce its Cognitive Complexity from 28 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Core/Search/SmithWatermanAligner.cs:19` | Refactor this method to reduce its Cognitive Complexity from 26 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Server/Program.cs` | Refactor this top-level file to reduce its Cognitive Complexity from 28 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:280` | Refactor this method to reduce its Cognitive Complexity from 28 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Generators/TraceLogDecoratorGenerator.cs:47` | Refactor this method to reduce its Cognitive Complexity from 52 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `csharpsquid:S3776` | `src/Cantus.Core/Parsers/LrcParser.cs:110` | Refactor this method to reduce its Cognitive Complexity from 39 to the 25 allowed. |
| Issue | MAJOR/MEDIUM | `css:S7924` | `docs/stylesheets/extra.css:156` | Text does not meet the minimal contrast requirement with its background. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `docs/stylesheets/extra.css:71` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `docs/stylesheets/extra.css:81` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `docs/stylesheets/extra.css:92` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9423` | `src/Cantus.Client/Cantus.Client/WasmScripts/index.html:205` | Remove this "!important" and resolve the specificity conflict instead. |
| Issue | MAJOR/MEDIUM | `css:S9440` | `src/Cantus.Client/Cantus.Client/WasmScripts/index.html:126` | Respect reduced-motion preferences here to avoid triggering motion sensitivity. |
| Issue | MAJOR/MEDIUM | `css:S9440` | `docs/stylesheets/extra.css:72` | Respect reduced-motion preferences here to avoid triggering motion sensitivity. |
| Issue | MAJOR/MEDIUM | `css:S9440` | `docs/stylesheets/extra.css:113` | Respect reduced-motion preferences here to avoid triggering motion sensitivity. |
| Issue | MAJOR/MEDIUM | `css:S9440` | `src/Cantus.Client/Cantus.Client/WasmScripts/index.html:101` | Respect reduced-motion preferences here to avoid triggering motion sensitivity. |
| Issue | MAJOR/MEDIUM | `javascript:S7762` | `src/Cantus.Client/Cantus.Client/WasmScripts/index.html:486` | Prefer `childNode.remove()` over `parentNode.removeChild(childNode)`. |
| Issue | MAJOR/MEDIUM | `python:S5778` | `.github/scripts/test_sonar_export.py:144` | Refactor this exception test to have only one invocation possibly throwing an exception. |
| Issue | MAJOR/MEDIUM | `python:S5778` | `.github/scripts/test_sonar_export.py:153` | Refactor this exception test to have only one invocation possibly throwing an exception. |
