---
hide:
  - navigation
  - toc
---

<div class="cantus-hero">
  <h1>Cantus</h1>
  <p class="tagline">
    Self-Hosted, Real-Time Synchronized Lyrics Display Platform.<br/>
    Continuous Spotify tracking, sub-millisecond clock sync, and beautiful karaoke-style visual animations.
  </p>
  <div class="cantus-actions">
    <a href="user-guide/" class="cantus-btn cantus-btn-primary">
      <span>Get Started</span> &rarr;
    </a>
    <a href="operator-guide/self-hosting/" class="cantus-btn cantus-btn-secondary">
      <span>Deploy with Docker</span>
    </a>
    <a href="architecture/overview/" class="cantus-btn cantus-btn-secondary">
      <span>Explore Architecture</span>
    </a>
  </div>
</div>

<div class="cantus-grid">
  <div class="cantus-card">
    <div class="cantus-card-icon">⚡</div>
    <h3>Sub-Millisecond Clock Sync</h3>
    <p>
      Continuous 4-timestamp NTP clock offset estimation and smooth interpolation filters network jitter to keep lyrics perfectly synchronized with your audio stream.
    </p>
    <a href="architecture/ntp-clock-sync/" class="card-link">Read about NTP Sync &rarr;</a>
  </div>

  <div class="cantus-card">
    <div class="cantus-card-icon">🧠</div>
    <h3>Adaptive Polling Engine</h3>
    <p>
      Intelligently modulates Spotify polling frequency (1.5s when playing, 5s when paused, 10s when idle) and sleeps when zero viewers are connected to conserve API rate limits.
    </p>
    <a href="architecture/adaptive-polling/" class="card-link">Learn about Adaptive Polling &rarr;</a>
  </div>

  <div class="cantus-card">
    <div class="cantus-card-icon">🎵</div>
    <h3>Multi-Tier Lyrics Caching</h3>
    <p>
      Instant local SQLite resolution, fallback to LRCLIB fuzzy matching, and negative caching for instrumental tracks with zero third-party tracking.
    </p>
    <a href="architecture/lyrics-caching/" class="card-link">Explore Lyrics Caching &rarr;</a>
  </div>

  <div class="cantus-card">
    <div class="cantus-card-icon">🎨</div>
    <h3>Dynamic Palette Theming</h3>
    <p>
      Extracts complementary accent colors and ambient gradients from active album artwork in real-time for an immersive listening environment.
    </p>
    <a href="user-guide/theming/" class="card-link">Discover Theming Engine &rarr;</a>
  </div>

  <div class="cantus-card">
    <div class="cantus-card-icon">🖥️</div>
    <h3>Cross-Platform Display</h3>
    <p>
      Native desktop application on Linux (Skia/X11) and Windows alongside WebAssembly in modern desktop, tablet, and TV web browsers.
    </p>
    <a href="user-guide/playback-and-display/" class="card-link">View Display Modes &rarr;</a>
  </div>

  <div class="cantus-card">
    <div class="cantus-card-icon">🔒</div>
    <h3>Privacy & Self-Hosted</h3>
    <p>
      Complete data ownership. OAuth tokens are encrypted at rest with ASP.NET Core Data Protection, running locally inside a lightweight multi-arch Docker container.
    </p>
    <a href="operator-guide/self-hosting/" class="card-link">Self-Hosting Guide &rarr;</a>
  </div>
</div>

---

## System Workflow Overview

```mermaid
sequenceDiagram
    autonumber
    actor Listener as Listener
    participant Client as Uno Client (Browser / TV / App)
    participant Hub as SignalR PlaybackHub
    participant Poller as Adaptive Playback Engine
    participant Spotify as Spotify Web API
    participant Cache as SQLite & LRCLIB

    Listener->>Client: Open Room / Connect
    Client->>Hub: JoinRoom & NTP Clock Sync
    Hub-->>Client: NTP Pong (Offset Estimation)
    Poller->>Spotify: Poll Current Playback State
    Spotify-->>Poller: Track ID, Progress, IsPlaying
    Poller->>Cache: Query Synced Lyrics (LRC)
    Cache-->>Poller: Parsed Line Timings
    Poller->>Hub: Broadcast Playback State & Lyrics
    Hub->>Client: Real-Time Stream (State + Lyrics)
    Client->>Client: Smooth Scroll & Active Line Highlight
```

---

## Documentation Tracks

<div class="cantus-grid">
  <div class="cantus-card">
    <h3>📖 User Guide</h3>
    <p>
      Learn how to link your Spotify account, customize the lyric display, configure fullscreen TV kiosk modes, and calibrate audio latency offsets.
    </p>
    <a href="user-guide/" class="card-link">Go to User Guide &rarr;</a>
  </div>

  <div class="cantus-card">
    <h3>🚀 Operator Guide</h3>
    <p>
      Deploy Cantus in your home lab or cloud VM using Docker Compose, setup reverse proxies (Caddy, Nginx, Traefik), and configure Spotify Developer apps.
    </p>
    <a href="operator-guide/" class="card-link">Go to Operator Guide &rarr;</a>
  </div>

  <div class="cantus-card">
    <h3>🏗️ Architecture & Concepts</h3>
    <p>
      Deep dive into the 5-layer system design, real-time SignalR hubs, NTP clock synchronization math, and Uno Platform MVVM rendering.
    </p>
    <a href="architecture/overview/" class="card-link">Explore Architecture &rarr;</a>
  </div>

  <div class="cantus-card">
    <h3>📚 Technical Reference</h3>
    <p>
      Complete reference manuals for SignalR PlaybackHub real-time protocol, REST Minimal APIs, Docker configuration, and environment variables.
    </p>
    <a href="reference/signalr-api/" class="card-link">View Technical Reference &rarr;</a>
  </div>
</div>
