# Configuration & Environment Reference

This page lists the environment variables and configuration options read by the Cantus server and desktop client.

---

## How Settings Are Read

Settings under a section (such as `Spotify` or `DisplayPairing`) can be set in `appsettings.json` or as environment variables, with `__` (double underscore) separating nested keys: `DisplayPairing__CodeLength=10`. Environment variables override `appsettings.json`.

The server also loads `appsettings.{Environment}.json` on top of `appsettings.json`. `ASPNETCORE_ENVIRONMENT` defaults to `Production` when unset, including for a local `dotnet run`, so `appsettings.Production.json` applies unless you set `ASPNETCORE_ENVIRONMENT=Development`.

Defaults below are the effective values: the shipped `appsettings.json` wins over the built-in code default where they differ.

---

## Environment Variables & Configuration Options

### Spotify Authentication
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`Spotify__ClientId`** | `string` | *(empty)* | 32-character Client ID of the server's Spotify app, from the Spotify Developer Dashboard. When unset, users sign in with their own Spotify app instead (see `Spotify__AllowUserClientIds`). | Recommended |
| **`Spotify__AllowUserClientIds`** | `bool` | *(unset)* | Lets users sign in with their own Spotify app's Client ID. Unset means allowed only when `Spotify__ClientId` is unset. `true` allows it alongside the server's app; `false` always turns it off. Anyone who can reach the server and has a Spotify app can then sign in, so leave it off on internet-facing servers unless you want that. See [Users' own Spotify apps](../operator-guide/spotify-setup.md#5-users-own-spotify-apps). | No |
| **`CANTUS_HOST_URL`** | `string` | *(unset)* | Public root URL of your instance, used to build the OAuth callback (`<url>/api/auth/spotify/callback`). `HOST_URL`, `BASE_URL` and `Spotify__BaseUrl` are accepted as fallbacks, in that order. When unset, the URL is taken from the incoming request (honouring trusted forwarded headers), then `http://localhost:5000`. Requests whose host is `localhost` or `127.0.0.1` always use their own address. The bundled `docker-compose.yml` sets it to `http://localhost:5000` unless you override it. | Recommended |
| **`Spotify__RedirectUri`** | `string` | `http://localhost:5000/api/auth/spotify/callback` | Explicit OAuth callback. Used only when `CANTUS_HOST_URL`, `HOST_URL` and `BASE_URL` are all unset and the value is not one of the stock `localhost:5000` / `127.0.0.1:5000` callbacks. | No |
| **`Spotify__Scopes__0`** | `string[]` | 5 scopes | OAuth scopes requested at login. The five built-in scopes (`user-read-playback-state`, `user-read-currently-playing`, `user-modify-playback-state`, `user-read-private`, `user-read-email`) are always requested: .NET binding appends configured entries to the built-in list, and duplicates are removed. You can add scopes but not remove the defaults. | No |
| **`Spotify__ClientSecret`** | `string` | *(empty)* | Not used. Cantus uses PKCE, which needs no secret. | No |

### Storage & Server
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`ConnectionStrings__CantusDatabase`** | `string` | `Data Source=/app/data/cantus.db` (`Production`), `Data Source=cantus.db` (other environments) | SQLite connection string. A relative path resolves against the working directory. The directory is created at startup if missing. | No |
| **`Database__BackupBeforeMigrations`** | `bool` | `true` | Before applying schema migrations to an existing database, copy it to `<database>.pre-<migration>.bak` next to it. The 3 newest copies are kept. If the copy fails, startup stops without migrating. Set to `false` to migrate without a copy. | No |
| **`DATA_DIR`** | `string` | `/app/data` in the Docker image | Parent of the Data Protection key ring (`$DATA_DIR/keys`). Does not move the database. When unset, `/app/data` is used if it exists; otherwise keys go to the ASP.NET Core default key store. | No |
| **`CANTUS_LOG_CONFIGURATION`** | `string` | `none` (Release builds), `debug` (Debug builds) | Log profile: `none`, `debug` or `trace` (case-insensitive). Unknown values fall back to the default. See [Logging](#logging-configurations-cli-parameters). | No |
| **`ASPNETCORE_ENVIRONMENT`** | `string` | `Production` | ASP.NET Core environment (`Development`, `Production`). `Development` also serves the OpenAPI document and Scalar API reference. | No |
| **`ASPNETCORE_URLS`** | `string` | `http://+:5000` in the Docker image, `http://localhost:5000` otherwise | Listening addresses. | No |

### Adaptive Playback Polling (`PlaybackPoller`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`PlaybackPoller__ActivePollIntervalMs`** | `int` | `4000` | Polling cadence (ms) while Spotify is playing. | No |
| **`PlaybackPoller__ApproachingEndPollIntervalMs`** | `int` | `2500` | Cadence (ms) when remaining track time is at most `ApproachingEndThresholdMs`. | No |
| **`PlaybackPoller__ImminentEndPollIntervalMs`** | `int` | `1200` | Cadence (ms) when remaining track time is at most `ImminentEndThresholdMs`. | No |
| **`PlaybackPoller__ApproachingEndThresholdMs`** | `int` | `15000` | Remaining time (ms) that starts approaching-end polling. | No |
| **`PlaybackPoller__ImminentEndThresholdMs`** | `int` | `5000` | Remaining time (ms) that starts imminent-end polling. | No |
| **`PlaybackPoller__PausedPollIntervalMs`** | `int` | `5000` | Cadence (ms) for the first minute of a pause. Also the delay before retrying after a failed poll. | No |
| **`PlaybackPoller__PausedExtendedPollIntervalMs`** | `int` | `15000` | Cadence (ms) after 1 to 5 minutes paused. | No |
| **`PlaybackPoller__PausedDeepPollIntervalMs`** | `int` | `30000` | Cadence (ms) after more than 5 minutes paused. | No |
| **`PlaybackPoller__IdlePollIntervalMs`** | `int` | `10000` | Cadence (ms) for the first 2 minutes with no active playback, and the loop delay while clients are connected but none is signed in. With no clients connected, the loop checks once a second (not configurable). | No |
| **`PlaybackPoller__IdleExtendedPollIntervalMs`** | `int` | `30000` | Cadence (ms) after 2 to 10 minutes idle. | No |
| **`PlaybackPoller__IdleDeepPollIntervalMs`** | `int` | `60000` | Cadence (ms) after more than 10 minutes idle. | No |
| **`PlaybackPoller__BackgroundPollIntervalMs`** | `int` | `20000` | Cadence (ms) while playing when every viewer of the owner reports hidden. When paused or idle and hidden, the larger of this and the matching deep interval is used. | No |
| **`PlaybackPoller__DiagnosticsBroadcastIntervalMs`** | `int` | `5000` | Not used: diagnostics are sent with every poll. | No |

### Display Pairing (`DisplayPairing`)
Settings for [pairing a Display](rest-api.md#display-endpoints). Values outside the allowed range stop the server at startup.

| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`DisplayPairing__CodeLength`** | `int` | `8` | Characters in a pairing code (6 to 16). | No |
| **`DisplayPairing__CodeLifetimeSeconds`** | `int` | `300` | Seconds a pairing code stays valid (30 to 3600). | No |
| **`DisplayPairing__MaxPendingPairings`** | `int` | `1000` | Pairings that may wait for approval at once, across all clients (at least 1). Further requests get `503`. | No |
| **`DisplayPairing__RateLimitPermitLimit`** | `int` | `10` | Pairing requests, and separately approvals, allowed per client address per window (at least 1). IPv6 addresses in the same `/64` count as one client. Over the limit returns `429`. | No |
| **`DisplayPairing__RateLimitWindowSeconds`** | `int` | `60` | Length of the fixed rate limit window in seconds (1 to 3600). | No |

### Local Sessions (`LocalSessions`)
Settings for [Local Sessions](../operator-guide/self-hosting.md#6-let-people-use-their-own-players), which show what a desktop app's own players are playing, with no Spotify account. Values outside the allowed range stop the server at startup.

| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`LocalSessions__Enabled`** | `bool` | `false` | Allows creating Local Sessions and publishing playback from them. Turning it off stops both; existing Local Sessions stay valid for viewing, managing Displays and signing out. They are not expired while it is off (see `IdleExpiryDays`). | No |
| **`LocalSessions__JoinCode`** | `string` | *(empty)* | Code required to create a Local Session. Blank means no code. A set code must be at least 8 characters. It is never sent to clients. Set one on internet-facing servers. | Recommended |
| **`LocalSessions__RateLimitPermitLimit`** | `int` | `5` | Local Session creations allowed per client address per window (at least 1). IPv6 addresses in the same `/64` count as one client. Over the limit returns `429`. | No |
| **`LocalSessions__RateLimitWindowSeconds`** | `int` | `600` | Length of the fixed creation rate limit window in seconds (1 to 86400). | No |
| **`LocalSessions__MaxSessions`** | `int` | `500` | Most Local Sessions stored at once (at least 1). Further creations get `503`. | No |
| **`LocalSessions__IdleExpiryDays`** | `int` | `90` | Days a Local Session may go without publishing before it is deleted, with its Displays (1 to 3650). Idle Sessions are only removed when someone next creates a Local Session, so none are removed while `Enabled` is off. | No |
| **`LocalSessions__PublisherGraceSeconds`** | `int` | `20` | Seconds a Local Session's playback stays live after its desktop app disconnects, before viewers see it paused (1 to 600). | No |

### Lyrics Provider: LRCLIB (`Lrclib`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`Lrclib__BaseUrl`** | `string` | `https://lrclib.net` | LRCLIB lyrics service base URL. | No |
| **`Lrclib__TimeoutSeconds`** | `int` | `8` | HTTP request timeout (seconds) for LRCLIB queries. | No |
| **`Lrclib__UserAgent`** | `string` | `CantusSyncedLyrics/1.0.0 (https://github.com/jav76/Cantus)` | HTTP User-Agent header sent to LRCLIB. | No |

### Lyrics Provider: NetEase Fallback (`Netease`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`Netease__Enabled`** | `bool` | `true` | Enables the NetEase Cloud Music fallback provider (unofficial API). Set `false` to run LRCLIB-only. | No |
| **`Netease__BaseUrl`** | `string` | `https://music.163.com` | NetEase web API base URL. Also sent as the `Referer` header. | No |
| **`Netease__UserAgent`** | `string` | `Mozilla/5.0 (Windows NT 10.0; Win64; x64)` | HTTP User-Agent header sent to the NetEase API. | No |
| **`Netease__TimeoutSeconds`** | `int` | `10` | HTTP request timeout (seconds) for NetEase queries. | No |
| **`Netease__SearchLimit`** | `int` | `10` | Maximum search candidates fetched per lookup. | No |
| **`Netease__DurationToleranceSeconds`** | `int` | `5` | Maximum track-duration mismatch (seconds) accepted when matching search results. | No |

### Lyrics Cache (`LyricsCache`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`LyricsCache__NegativeCacheDays`** | `int` | `30` | Days to cache negative lookups (instrumental tracks, or not found across all providers). | No |
| **`LyricsCache__WordTimingUpgradeMaxAttempts`** | `int` | `3` | How many times a cached entry without word timing is re-fetched from word-timing providers (NetEase) to look for word-timed lyrics. `0` turns upgrades off; they are also skipped when NetEase is disabled. | No |
| **`LyricsCache__WordTimingUpgradeIntervalDays`** | `int` | `7` | Minimum days between word-timing upgrade attempts for the same track. | No |

### Song Lookup (`SongLookup`)
Settings for [finding a song by lyrics](rest-api.md#song-lookup-endpoints). Values outside the allowed range stop the server at startup.

| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`SongLookup__ExternalSearchEnabled`** | `bool` | `true` | Lets lookups call online lyrics search (NetEase, LRCLIB). Set `false` to search only the server's own lyrics library; no fragment then leaves the server. | No |
| **`SongLookup__LrclibTitleGuessEnabled`** | `bool` | `true` | Lets lookups try parts of the fragment as an LRCLIB track title. NetEase is unaffected. | No |
| **`SongLookup__OnlineLookupsPerMinute`** | `int` | `6` | Lookups per Spotify user per rolling minute that may call a lyrics provider (1 to 60). Library-only searches and cached answers do not count. | No |
| **`SongLookup__LrclibRequestsPerMinute`** | `int` | `10` | Lookup requests sent to LRCLIB per rolling minute, server-wide (1 to 60). Now-playing lyric fetches are not counted or refused. | No |
| **`SongLookup__NeteaseRequestsPerMinute`** | `int` | `6` | Lookup requests sent to NetEase per rolling minute, server-wide (1 to 60). Now-playing lyric fetches are not counted or refused. | No |
| **`SongLookup__SearchPermitLimit`** | `int` | `20` | Search requests allowed per client address per window (at least 1). IPv6 addresses in the same `/64` count as one client. | No |
| **`SongLookup__PlayPermitLimit`** | `int` | `12` | Play and queue requests allowed per client address per window (at least 1). | No |
| **`SongLookup__RateLimitWindowSeconds`** | `int` | `60` | Length of the search and play rate limit windows in seconds (1 to 3600). | No |

`Netease__Enabled=false` also removes NetEase from lookups.

### Cross-Origin Requests (`Cors`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`Cors__AllowedOrigins__0`** | `string[]` | *(empty)* | Origins permitted to call the API cross-origin with credentials. Empty by default: the bundled web client is served from the same origin and needs no grant. Only set this when hosting the client separately (e.g. `https://lyrics.mydomain.com`). Use `__1`, `__2` for more entries. | No |

### Reverse Proxy Trust (`ForwardedHeaders`)
| Variable | Type | Default | Description | Required |
| :--- | :---: | :---: | :--- | :---: |
| **`ForwardedHeaders__TrustAllProxies`** | `bool` | `false` | Honour `X-Forwarded-*` from any remote address. Only enable when clients cannot reach Cantus directly (proxy-only exposure); otherwise any client can spoof its address and scheme. Overrides the two lists below. | No |
| **`ForwardedHeaders__KnownProxies__0`** | `string[]` | *(empty)* | Additional proxy IP addresses to trust, beyond the default loopback. Entries that do not parse are skipped silently. | No |
| **`ForwardedHeaders__KnownNetworks__0`** | `string[]` | *(empty)* | Proxy networks to trust in CIDR form (e.g. `172.16.0.0/12` for a Docker bridge network). Entries that do not parse are skipped silently. | No |

---

## Logging Configurations & CLI Parameters

The server and the desktop client both take a log profile: `none`, `debug` or `trace`. The CLI option wins over `CANTUS_LOG_CONFIGURATION`. Without either, Release builds (the Docker image and release downloads) use `none` and Debug builds (a plain `dotnet run`) use `debug`.

### CLI Option: `--log-configuration` (alias: `-l`)
```bash
# Server with debug logging (console + rolling file + SQLite LogEntries table)
dotnet run --project src/Cantus.Server -- --log-configuration debug

# Desktop client with trace logging (console only)
./Cantus-Linux-x64.AppImage --log-configuration trace
```

In Docker, set `CANTUS_LOG_CONFIGURATION=debug` in the service environment.

### CLI Option: `--server-url` (alias: `-s`, Desktop client only)
Points the desktop client at a Cantus server for this launch. Accepts the same address you would open in a browser; the SignalR hub path (`/hubs/playback`) is appended when missing and any query string is dropped. Also readable from the `CANTUS_SERVER_URL` environment variable. A value that is not an absolute `http`/`https` URL is ignored with a warning rather than preventing startup.

The desktop client picks its server in this order:

| Order | Source | Notes |
| :--- | :--- | :--- |
| 1 | `--server-url` | An invalid value is ignored and does not fall through to the environment variable. |
| 2 | `CANTUS_SERVER_URL` | Used when the CLI option is absent or blank. |
| 3 | Saved **Server** setting | Set by the first-launch prompt or under **Server** in the app's settings. Stored per device as `cantus_server_url`. |
| 4 | `http://127.0.0.1:5000` | The default. On a first launch the app asks for the address instead, unless the prompt was skipped before. |

With source 1 or 2 the first-launch prompt is not shown, nothing is saved, and the **Server** setting is read-only for that launch. See [Desktop App: Choose Your Server](../user-guide/desktop-app.md).

```bash
# Connect the desktop client to a server behind an HTTPS reverse proxy
./Cantus-Linux-x64.AppImage --server-url https://lyrics.example.com

# Or via the environment
CANTUS_SERVER_URL=https://lyrics.example.com ./Cantus-Linux-x64.AppImage
```

> **Spotify login needs HTTPS for remote servers.** Spotify only accepts `http://` redirect URIs on loopback addresses (`127.0.0.1` or `[::1]`), never `localhost` or a LAN IP. A plain `http://192.168.0.10:5000` server will connect, but Spotify login through it fails. Put remote servers behind HTTPS (see [Reverse Proxy](../operator-guide/reverse-proxy.md)).

> The WebAssembly client ignores both options: it is served by the Cantus server, so it always connects back to its own origin and uses the build's default log profile.

### Environment Variable: `CANTUS_LOCAL_JOIN_CODE` (Desktop client only)
Prefills the join code when you choose **Use this computer's players** on a server that requires one (`LocalSessions__JoinCode`). Optional: without it, Cantus asks for the code. See [Use This Computer's Players](../user-guide/local-players.md).

### Server Log Outputs
| Profile | Console / Stdout | Rolling File | SQLite (`LogEntries` table) | `[TraceLog]` method tracing |
| :--- | :--- | :---: | :---: | :---: |
| **`none`** | `Information` and above (`Microsoft.*`, `System.*`: `Warning` and above) | Off | Off | Off |
| **`debug`** | `Debug` and above (`Microsoft.*`, `System.*`: `Information` and above) | On | On | Off |
| **`trace`** | Everything | On | On | On (entry, exit, timing) |

- The rolling file is `cantus-server.log` in `<temp>/cantus/logs/` (`/tmp/cantus/logs/` on Linux and in the container, which is outside the `/app/data` volume). It rolls daily and at 10 MB, keeping 10 backups.
- The `LogEntries` table lives in the database set by `ConnectionStrings__CantusDatabase`.
- The `Logging:LogLevel` section of `appsettings*.json` has no effect: the log profile replaces those filter rules.

The desktop client logs to the console only: `Information` (`none`), `Debug` (`debug`) or `Trace` (`trace`), with `Uno.*`, `Windows.*` and `Microsoft.*` held at `Warning`.

Logs identify a Session by `{SessionRef}`, a short hash, never by its id. Rows written before this change may contain Session ids: see [Session ids in logs from older releases](../operator-guide/troubleshooting.md#session-ids-in-logs-from-older-releases).

---

## Storage & File Layout

In the Docker image, `/app/data` must be writable:

```text
/app/data/
├── cantus.db                 # SQLite database (Sessions, Displays, lyrics cache, track offsets, lyrics overrides, debug logs)
├── cantus.db-shm             # SQLite shared memory file (WAL mode)
├── cantus.db-wal             # SQLite write-ahead log
└── keys/                     # Data Protection key ring for OAuth token encryption at rest
    └── key-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.xml
```

---

## Ports & Networking

| Port | Protocol | Default Purpose |
| :---: | :---: | :--- |
| **`5000`** | `TCP (HTTP/WS)` | REST APIs, the SignalR `PlaybackHub` (`/hubs/playback`) and the static WebAssembly client. |
