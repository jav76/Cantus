## SonarQube Quality Report

- **Project**: `Cantus`
- **Quality Gate**: **OK**
- **Generated (UTC)**: `2026-11-25T00:02:05Z`
- **Repository**: https://github.com/jav76/Cantus
- **Public Dashboard**: https://jav76.github.io/Cantus/

| Metric | Value |
| :--- | :--- |
| Quality Gate | OK |
| Reliability Rating / Issues | A (0) |
| Security Rating / Issues | A (0) |
| Maintainability Rating / Issues | A (2394) |
| Coverage | 74% |
| Duplicated Lines | 0.5% |
| Lines of Code (NCLOC) | 34.5k |
| Technical Debt | 3h9min |
| Open Issues (total) | 2394 |
| Open Security Hotspots | 0 |
| SARIF Exported Findings | 0 |

### Quality Gate Conditions

| Metric | Status | Actual | Threshold |
| :--- | :--- | :--- | :--- |
| `new_coverage` | OK | 69.6 | LT 50 |
| `new_duplicated_lines_density` | OK | 0.0 | GT 5 |
| `new_software_quality_reliability_rating` | OK | 1 | GT 2 |
| `new_software_quality_security_rating` | OK | 1 | GT 2 |

### Top Findings (Major / MEDIUM+ and Open Hotspots)

| Kind | Severity | Rule | Location | Message |
| :--- | :--- | :--- | :--- | :--- |
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
