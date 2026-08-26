# System Architecture Overview

Cantus is built following **Clean Architecture** principles to separate concerns, ensure testability, and isolate external third-party dependencies (such as Spotify Web API and LRCLIB) from core domain business logic.

---

## 5-Layer Architectural Design

The system is organized into five distinct layers:

```mermaid
graph TB
    subgraph Client Presentation Layer
        UnoUI["Uno Platform Client<br/>(WASM Browser / Linux Skia / Windows)"]
        VM["MVVM ViewModels<br/>(LyricsViewModel, LyricLineViewModel)"]
        UnoUI --> VM
    end

    subgraph Server Engine & Application Layer
        Hub["SignalR PlaybackHub<br/>(WebSocket Real-Time Broadcast)"]
        Engine["Adaptive Polling Engine<br/>(ActiveUsersPlaybackMonitor)"]
        Endpoints["ASP.NET Core Minimal APIs<br/>(/api/auth, /api/health)"]
    end

    subgraph Core Domain Layer
        Models["Domain Models<br/>(PlaybackState, SyncedLyrics, LyricLine)"]
        Parser["LRC Parser Engine<br/>(LrcParser, Timestamp Normalizer)"]
        Interfaces["Repository Interfaces<br/>(ILyricsCacheRepository, ISessionRepository)"]
    end

    subgraph Infrastructure & Persistence Layer
        Spotify["Spotify Integration<br/>(SpotifyAuthService, SpotifyApiClient)"]
        LRCLIB["Lyrics Provider<br/>(LrclibLyricsProvider)"]
        DB["SQLite Database & EF Core<br/>(CantusDbContext)"]
        Crypto["ASP.NET Core Data Protection<br/>(DataProtectionTokenEncryptionService)"]
    end

    subgraph DevOps & Deployment
        Docker["Multi-Arch Container<br/>(amd64 / arm64)"]
    end

    Client Presentation Layer -->|SignalR / HTTP| Server Engine & Application Layer
    Server Engine & Application Layer --> Core Domain Layer
    Server Engine & Application Layer --> Infrastructure & Persistence Layer
    Infrastructure & Persistence Layer --> Core Domain Layer
    DevOps & Deployment -. Hosts .-> Server Engine & Application Layer
    DevOps & Deployment -. Serves .-> Client Presentation Layer
```

---

## Architectural Layers Explained

### 1. Client Presentation (Uno Platform)
- **Technology**: Uno Platform (C# / XAML), Skia Linux/Windows, WebAssembly.
- **Responsibilities**: Renders high-frame-rate scrolling lyrics, extracts dynamic color palettes from album artwork, performs local sub-millisecond clock interpolation, and dispatches UI events.

### 2. Server Engine & Real-Time Hub (ASP.NET Core)
- **Technology**: ASP.NET Core 9 Minimal APIs, Microsoft SignalR.
- **Responsibilities**: Coordinates connected client display rooms, manages the background adaptive polling loop, orchestrates Spotify token renewal, and handles 4-timestamp NTP clock sync pings.

### 3. Core Domain Models & Contracts
- **Technology**: Pure .NET 9 Standard library (Zero external framework dependencies).
- **Responsibilities**: Contains entity definitions (`PlaybackState`, `SyncedLyrics`, `LyricLine`, `UserSession`), parsing algorithms (`LrcParser`), and abstract provider/repository contracts.

### 4. Infrastructure & External Services
- **Technology**: Entity Framework Core with SQLite, ASP.NET Core Data Protection, `HttpClient`.
- **Responsibilities**: Implements Spotify OAuth PKCE exchanges and token renewal, queries LRCLIB for synchronized lyrics with fuzzy matching, manages 30-day positive caching and 7-day negative caching for instrumental songs, and encrypts OAuth refresh tokens at rest.

### 5. DevOps & Containerization
- **Technology**: Multi-stage Docker build, Docker Compose, GitHub Actions.
- **Responsibilities**: Compiles the Uno WebAssembly static bundle and ASP.NET Core backend into a single unified container for effortless self-hosting.

---

## Core Conceptual Deep Dives

To explore specific subsystems in depth:

- [**NTP Clock Synchronization**](ntp-clock-sync.md): How Cantus synchronizes client display clocks with sub-millisecond accuracy.
- [**Adaptive Polling Engine**](adaptive-polling.md): How Cantus tracks Spotify playback efficiently without exceeding API rate limits.
- [**Multi-Tier Lyrics Caching**](lyrics-caching.md): How lyrics are resolved, cached, and validated.
- [**Uno Platform Client Architecture**](client-uno.md): How the MVVM pattern and cross-platform UI rendering work under the hood.
