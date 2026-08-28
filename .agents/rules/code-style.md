---
trigger: always_on
---

# Cantus Code Style & Modern C# Standards

Adhere to the following conventions when authoring, modifying, or refactoring C# code across the Cantus workspace.

---

## 1. Type Declarations & `var`
- **Explicit Types Over `var`**: Do not use `var` for local variable declarations. Always use the explicit type (e.g. `string query = "..."`, `int count = 0`, `LyricLine? line = null`).
- **Target-Typed `new()`**: When the explicit type is already declared on the left-hand side, target-typed `new()` is preferred for constructor invocations to eliminate redundancy:
  ```csharp
  // Correct
  List<LyricLine> lines = new();
  Dictionary<string, CachedTrack> cache = new();
  LyricSyncEngine engine = new(client, logger);

  // Avoid
  var lines = new List<LyricLine>();
  List<LyricLine> lines = new List<LyricLine>();
  ```

---

## 2. Namespaces & `using` Directives
- **File-Scoped Namespaces**: Always use file-scoped namespace declarations (`namespace Cantus.Core;`) to save horizontal indentation.
- **`using` Directive Placement & Ordering**: Place `using` directives outside the namespace at the top of the file. Sort `System` and `System.*` directives first, followed alphabetically by other namespaces. Remove unused directives.
- **Global Usings**: Confine global usings to dedicated files (e.g., `GlobalUsings.cs`) for ubiquitous namespaces only.

---

## 3. Braces, Line Width & Layout (Allman Style)
- **Allman Bracing**: Opening braces `{` must always be placed on their own line at the same indentation level as the parent declaration (classes, methods, properties, control flow statements).
- **Line Length Target**: Target line widths under 120 characters. Sensible exceptions apply to long URL strings, regex patterns, or attribute annotations.
- **Whitespace & Formatting**: Clean whitespace and blank lines to separate logical blocks or enhance clarity are encouraged.

---

## 4. Parameter & Invocation Wrapping
- **Multi-Line Signatures & Calls**: When a method signature, constructor definition, or method invocation wraps across lines, place each parameter/argument on its own line indented by 4 spaces:
  ```csharp
  public async Task<LyricResponse> FetchLyricsAsync(
      string isrc,
      string trackName,
      string artistName,
      CancellationToken cancellationToken = default)
  {
      // ...
  }
  ```
- **Fluent / LINQ Chains**: Wrap multi-step LINQ or builder invocations so that each method call starts on a new indented line:
  ```csharp
  var filtered = lines
      .Where(l => l.StartTimeMs >= startOffset)
      .OrderBy(l => l.StartTimeMs)
      .ToList();
  ```
- **Initializers**: Multi-line object and collection initializers should place each property/element on its own line with a trailing comma.

---

## 5. Expression-Bodied Members vs Block Bodies
- **Single-Line Members**: Use expression bodies (`=>`) for single-line properties, getters, indexers, and short single-line helper methods:
  ```csharp
  public bool IsActive => _activeCount > 0;
  public string GetDisplayText() => $"{Title} - {Artist}";
  ```
- **Multi-Line Members & Constructors**: Use full block bodies with braces for multi-line methods and all constructors.

---

## 6. Pattern Matching, Switch Expressions & Null Checking
- **Null Checking**: Use `is null` and `is not null` instead of `== null` / `!= null`.
- **Pattern Matching**: Prefer type pattern matching (`if (item is LyricWord word)`) over `as` casting followed by null checks.
- **Switch Expressions**: Prefer switch expressions (`state switch { ... }`) when mapping or returning values over traditional `switch` statements:
  ```csharp
  string statusText = state switch
  {
      PlaybackState.Playing => "Playing",
      PlaybackState.Paused => "Paused",
      _ => "Stopped"
  };
  ```

---

## 7. Naming, Modifiers & Qualification
- **Private Fields**: Prefix private and internal instance fields with an underscore and use `_camelCase` (e.g. `private readonly ILyricsProvider _lyricsProvider;`).
- **Constants**: Constants (`const`) must use `CAPS_CASE` / `SCREAMING_SNAKE_CASE` (e.g. `public const string SECTION_NAME = "Spotify";`, `private const int MAX_NTP_SAMPLES = 5;`).
- **No `this.` Qualifier**: Avoid `this.` or `Me.` qualification unless strictly necessary to disambiguate shadowed identifiers.
- **Explicit Accessibility**: Always explicitly declare accessibility modifiers (`private`, `public`, `protected`, `internal`) on all types and members.
- **`readonly` Modifier**: Apply `readonly` to all fields and properties that are assigned only during declaration or in the constructor.

---

## 8. Modern Types & Primary Constructors
- **Records**: Use `record` or `record struct` for immutable data models, DTOs, and event payloads.
- **Primary Constructors**: Use primary constructors for records/DTOs and concise service classes. Use traditional constructor blocks when initialization requires input validation, defensive copying, or complex setup logic.

---

## 9. Logging Standards, Conventions & Tracing Attributes
- **Logging Subsystem**: Cantus uses a mixed logging framework backed by `log4net`, routed through `Microsoft.Extensions.Logging`.
- **Log Level Guidelines**:
  - `Trace`: Granular method entry/exit parameters, timing, and internal state. Generated automatically on decorated interfaces via `[TraceLog]`.
  - `Debug`: Cache hits/misses, background poller ticks, detailed operational diagnostics.
  - `Information`: Milestone business events (user authenticated, room joined, session connected). Keep concise and informative.
  - `Warning`: Recoverable issues, retry attempts, transient external API / network errors.
  - `Error`: Unhandled failures, route exceptions (handled globally by `GlobalExceptionHandler`), critical sync breaks.
- **Method Tracing via `[TraceLog]`**:
  - Apply `[TraceLog]` to core interfaces in `Cantus.Core` or client services to auto-generate compile-time decorators with `Stopwatch` execution timing and parameter tracking.
  - Apply `[Redact]` to sensitive method parameters (tokens, credentials, secrets) to mask them as `[REDACTED]` in trace output.
- **Structured Message Templates**:
  - Never use string interpolation (`$"..."`) within `ILogger` calls. Use semantic message templates with named placeholders:
    ```csharp
    // Correct
    _logger.LogInformation("User {UserId} joined room {RoomCode}", userId, roomCode);

    // Avoid
    _logger.LogInformation($"User {userId} joined room {roomCode}");
    ```
- **CLI Logging Control**:
  - Desktop client and server support `--log-configuration <none|debug|trace>` (or `-l <level>`) and `CANTUS_LOG_CONFIGURATION` environment variable.

