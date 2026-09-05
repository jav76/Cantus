# Configuration & Environment Reference

This document lists all environment variables and configuration options supported by the Cantus server.

---

## Environment Variables

| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`SPOTIFY_CLIENT_ID`** / **`Spotify__ClientId`** | `string` | — | 32-character Client ID from your Spotify Developer Dashboard. | **Yes** |
| **`CANTUS_HOST_URL`** | `string` | `http://localhost:5000` | Public root URL of your Cantus instance (used for OAuth redirect callbacks). | **Yes** |
| **`Spotify__ClientSecret`** | `string` | — | Optional Spotify Client Secret (for developer override). | No |
| **`DATA_DIR`** | `string` | `/app/data` | Base directory for persistent database and encryption keys. | No |
| **`ConnectionStrings__CantusDatabase`** | `string` | `Data Source=cantus.db` | SQLite connection string. | No |
| **`PlaybackPoller__ActivePollIntervalMs`** | `int` | `1500` | Polling cadence (ms) when Spotify is actively playing. | No |
| **`PlaybackPoller__PausedPollIntervalMs`** | `int` | `5000` | Polling cadence (ms) when Spotify playback is paused. | No |
| **`PlaybackPoller__IdlePollIntervalMs`** | `int` | `10000` | Polling cadence (ms) when Spotify has been inactive > 60s. | No |
| **`PlaybackPoller__DiagnosticsBroadcastIntervalMs`** | `int` | `5000` | Interval (ms) for SignalR diagnostics broadcast telemetry. | No |
| **`Lrclib__BaseUrl`** | `string` | `https://lrclib.net` | LRCLIB lyrics service base URL. | No |
| **`Lrclib__NegativeCacheDays`** | `int` | `7` | Duration in days to cache tracks confirmed to have no lyrics. | No |
| **`CANTUS_LOG_CONFIGURATION`** | `string` | `none` | Logging configuration level: `none`, `debug`, or `trace`. | No |
| **`ASPNETCORE_ENVIRONMENT`** | `string` | `Production` | ASP.NET Core environment mode (`Development`, `Staging`, `Production`). | No |
| **`ASPNETCORE_URLS`** | `string` | `http://+:5000` | Listening binding address and port. | No |

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
