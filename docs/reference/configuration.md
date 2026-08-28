# Configuration & Environment Reference

This document lists all environment variables and configuration options supported by the Cantus server.

---

## Environment Variables

| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`SPOTIFY_CLIENT_ID`** | `string` | — | 32-character Client ID from your Spotify Developer Dashboard. | **Yes** |
| **`CANTUS_HOST_URL`** | `string` | `http://localhost:5000` | Public root URL of your Cantus instance (used for OAuth redirect callbacks). | **Yes** |
| **`ASPNETCORE_ENVIRONMENT`** | `string` | `Production` | ASP.NET Core environment mode (`Development`, `Staging`, `Production`). | No |
| **`ASPNETCORE_URLS`** | `string` | `http://+:5000` | Listening binding address and port. | No |
| **`CANTUS_DEFAULT_LATENCY_OFFSET_MS`** | `int` | `0` | Base latency offset in milliseconds applied to all lyric renders (useful for persistent Bluetooth delay). | No |
| **`CANTUS_DB_PATH`** | `string` | `/app/data/cantus.db` | Absolute or relative file path to the SQLite database. | No |
| **`CANTUS_DATA_PROTECTION_DIR`** | `string` | `/app/data/DataProtection-Keys` | Directory path where ASP.NET Core Data Protection cryptographic keys are stored. | No |
| **`CANTUS_POLL_INTERVAL_PLAYING_MS`** | `int` | `500` | Polling cadence when Spotify is actively playing. | No |
| **`CANTUS_POLL_INTERVAL_PAUSED_MS`** | `int` | `3000` | Polling cadence when Spotify playback is paused. | No |
| **`CANTUS_POLL_INTERVAL_IDLE_MS`** | `int` | `10000` | Polling cadence when Spotify has been inactive > 60s. | No |
| **`CANTUS_LOG_CONFIGURATION`** | `string` | `none` | Logging configuration level: `none`, `debug`, or `trace`. | No |

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

```
/app/data/
├── cantus.db                 # SQLite database (Sessions, Cache, Track Offsets)
├── cantus.db-shm             # SQLite shared memory file (WAL mode)
├── cantus.db-wal             # SQLite write-ahead log
└── DataProtection-Keys/      # XML Keyring for OAuth token encryption at rest
    └── key-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.xml
```

---

## Ports & Networking

| Port | Protocol | Default Purpose |
| :---: | :---: | :--- |
| **`5000`** | `TCP (HTTP/WS)` | Web server listening port for both REST APIs, SignalR PlaybackHub, and static WebAssembly client assets. |
