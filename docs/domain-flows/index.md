# Business Domain Flows

Cantus models core platform operations as structured **Domain Flows**. Each flow maps end-to-end business logic, trigger conditions, step sequences, and exact source code locations.

## Domain Map

| Domain | Summary | Key Entities | Business Flows |
| :--- | :--- | :--- | :--- |
| **[Spotify Authentication & Session Management](spotify-pkce-login.md)** | Handles Spotify OAuth 2.0 PKCE authentication flows, secure token encryption at rest via ASP.NET Core Data Protection, automated token renewal cycles, and user session persistence. | `UserSession`, `SpotifyAuthTokens`, `PKCEChallenge` | [View Flow](spotify-pkce-login.md) |
| **[Real-Time Playback Monitoring & Polling](playback-sync.md)** | Manages intelligent adaptive background polling of active Spotify player instances, tracks track transitions and play/pause states, and coordinates broadcasts across SignalR clients. | `PlaybackState`, `TrackInfo`, `UserPlaybackSnapshot` | [View Flow](playback-sync.md) |
| **[Synchronized Lyrics Retrieval & Caching](lyrics-caching.md)** | Orchestrates multi-tiered lyrics retrieval with local SQLite caching, LRCLIB integration with title/artist fuzzy matching, LRC parsing, and per-track latency calibration. | `SyncedLyrics`, `LyricLine`, `CachedLyricsEntity`, `TrackOffset` | [View Flow](lyrics-caching.md) |
| **[Client Clock Synchronization & Dynamic Rendering](ntp-interpolation.md)** | Provides continuous NTP-based sub-millisecond clock synchronization, playback position interpolation, dynamic color scheme extraction from album art, and smooth lyric UI rendering. | `NtpSample`, `AppTheme`, `LyricLineViewModel`, `DiagnosticsDto` | [View Flow](ntp-interpolation.md) |

## End-to-End System Interaction

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Client as Uno Platform Client
    participant Hub as SignalR PlaybackHub
    participant Poller as ActiveUsersPlaybackMonitor
    participant Spotify as Spotify Web API
    participant Cache as SQLite Lyrics Cache
    participant LRCLIB as LRCLIB Lyrics API

    User->>Client: Connect to Cantus Room
    Client->>Hub: Join Room / Sync Clock (NTP ping)
    Hub-->>Client: NTP pong (server timestamps)
    Poller->>Spotify: Poll active playback state
    Spotify-->>Poller: Current track, progress, is_playing
    Poller->>Cache: Query cached lyrics for trackId
    alt Cache Miss
        Cache->>LRCLIB: Query lyrics by title/artist
        LRCLIB-->>Cache: Raw LRC synced text
        Cache->>Cache: Store in SQLite with 30-day expiry
    end
    Poller->>Hub: Broadcast PlaybackState + SyncedLyrics
    Hub->>Client: Send real-time state & parsed lyric lines
    Client->>Client: Interpolate clock position & scroll active lyric
```
