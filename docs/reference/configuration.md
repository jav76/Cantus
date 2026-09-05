# Configuration & Environment Reference

This document lists all environment variables and configuration options supported by the Cantus server.

---

## Environment Variables & Configuration Options

All settings can be configured via environment variables (using double-underscore `__` syntax for nested keys) or in `appsettings.json`.

### Spotify Authentication
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`SPOTIFY_CLIENT_ID`** / **`Spotify__ClientId`** | `string` | — | 32-character Client ID from your Spotify Developer Dashboard. | **Yes** |
| **`CANTUS_HOST_URL`** | `string` | `http://localhost:5000` | Public root URL of your Cantus instance (used for OAuth redirect callbacks). | **Yes** |
| **`Spotify__ClientSecret`** | `string` | — | Optional Spotify Client Secret (for developer override). | No |
| **`Spotify__RedirectUri`** | `string` | `http://localhost:5000/api/auth/spotify/callback` | Default OAuth PKCE redirect URI. | No |

### Storage & Server
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`DATA_DIR`** | `string` | `/app/data` | Base directory for persistent database and encryption keys. | No |
| **`ConnectionStrings__CantusDatabase`** | `string` | `Data Source=cantus.db` | SQLite database connection string. | No |
| **`CANTUS_LOG_CONFIGURATION`** | `string` | `none` | Logging verbosity level: `none`, `debug`, or `trace`. | No |
| **`ASPNETCORE_ENVIRONMENT`** | `string` | `Production` | ASP.NET Core runtime environment profile (`Development`, `Production`). | No |
| **`ASPNETCORE_URLS`** | `string` | `http://+:5000` | Listening HTTP/WS binding addresses and port. | No |

### Adaptive Playback Polling (`PlaybackPoller`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`PlaybackPoller__ActivePollIntervalMs`** | `int` | `4000` | Baseline polling cadence (ms) when Spotify is actively playing. | No |
| **`PlaybackPoller__ApproachingEndPollIntervalMs`** | `int` | `2500` | Accelerated cadence (ms) when remaining track duration $\le$ `ApproachingEndThresholdMs`. | No |
| **`PlaybackPoller__ImminentEndPollIntervalMs`** | `int` | `1200` | Accelerated cadence (ms) when remaining track duration $\le$ `ImminentEndThresholdMs`. | No |
| **`PlaybackPoller__ApproachingEndThresholdMs`** | `int` | `15000` | Remaining track duration threshold (ms) to engage approaching-end acceleration. | No |
| **`PlaybackPoller__ImminentEndThresholdMs`** | `int` | `5000` | Remaining track duration threshold (ms) to engage imminent-end acceleration. | No |
| **`PlaybackPoller__PausedPollIntervalMs`** | `int` | `5000` | Initial polling cadence (ms) when playback is paused ($\le$ 1 min). | No |
| **`PlaybackPoller__PausedExtendedPollIntervalMs`** | `int` | `15000` | Extended polling cadence (ms) when playback has been paused 1–5 min. | No |
| **`PlaybackPoller__PausedDeepPollIntervalMs`** | `int` | `30000` | Deep conservation polling cadence (ms) when playback has been paused $>$ 5 min. | No |
| **`PlaybackPoller__IdlePollIntervalMs`** | `int` | `10000` | Initial polling cadence (ms) when no active playback is detected ($\le$ 2 min). | No |
| **`PlaybackPoller__IdleExtendedPollIntervalMs`** | `int` | `30000` | Extended polling cadence (ms) when inactive for 2–10 min. | No |
| **`PlaybackPoller__IdleDeepPollIntervalMs`** | `int` | `60000` | Deep conservation cadence (ms) when inactive $>$ 10 min. | No |
| **`PlaybackPoller__BackgroundPollIntervalMs`** | `int` | `20000` | Polling cadence (ms) applied when all connected viewer tabs report hidden/minimized. | No |
| **`PlaybackPoller__DiagnosticsBroadcastIntervalMs`** | `int` | `5000` | Interval (ms) for SignalR telemetry diagnostics broadcast. | No |

### Lyrics Provider (`Lrclib`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`Lrclib__BaseUrl`** | `string` | `https://lrclib.net` | LRCLIB lyrics service base URL. | No |
| **`Lrclib__NegativeCacheDays`** | `int` | `30` | Duration in days to cache negative lookups (instrumental tracks / not found). | No |
| **`Lrclib__TimeoutSeconds`** | `int` | `8` | HTTP request timeout (seconds) for external LRCLIB queries. | No |
| **`Lrclib__UserAgent`** | `string` | `CantusSyncedLyrics/1.0.0 (https://github.com/cantus)` | HTTP User-Agent header sent to LRCLIB API. | No |

### Playback Interpolator (`PlaybackInterpolator`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`PlaybackInterpolator__SeekThresholdMs`** | `int` | `2000` | Delta threshold (ms) beyond which progress changes are treated as seeks rather than drift. | No |
| **`PlaybackInterpolator__DriftToleranceMs`** | `int` | `500` | Allowable drift (ms) before progressive correction steering engages. | No |
| **`PlaybackInterpolator__DriftCorrectionFraction`** | `double` | `0.2` | Fraction of remaining drift corrected per calculation step. | No |

---

## Logging Configurations & CLI Parameters

Cantus supports configurable log verbosity across both the Server and the Desktop client.

### CLI Option: `--log-configuration` (alias: `-l`)
```bash
# Start server with debug logging (console + rolling file + SQLite database)
dotnet run --project src/Cantus.Server -- --log-configuration debug

# Start desktop client with trace logging (console + rolling file)
./Cantus-Linux-x64.AppImage --log-configuration trace
```

### Log Levels & Output Matrix
| Configuration | Console / Stdout | Rolling File (`%tmp%/cantus/logs`) | SQLite Database (`LogEntries` Table) | Tracing (`[TraceLog]`) |
| :--- | :---: | :---: | :---: | :---: |
| **`none`** (Default) | Errors & Warnings | Disabled | Disabled | Disabled |
| **`debug`** | Information & Debug | Enabled (`cantus-*.log`) | Enabled (Server) | Disabled |
| **`trace`** | Full Trace Stream | Enabled (`cantus-*.log`) | Enabled (Server) | Enabled (Method Entry/Exit/Timing) |

---

## Storage & File Layout

When running Cantus, the application expects the `/app/data` volume to be writable:

```text
/app/data/
├── cantus.db                 # SQLite database (Sessions, Cache, Track Offsets)
├── cantus.db-shm             # SQLite shared memory file (WAL mode)
├── cantus.db-wal             # SQLite write-ahead log
└── keys/                     # XML Keyring for OAuth token encryption at rest
    └── key-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.xml
```

---

## Ports & Networking

| Port | Protocol | Default Purpose |
| :---: | :---: | :--- |
| **`5000`** | `TCP (HTTP/WS)` | Web server listening port for both REST APIs, SignalR PlaybackHub, and static WebAssembly client assets. |
