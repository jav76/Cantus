# Cantus

[![CI](https://github.com/jav76/Cantus/actions/workflows/ci.yml/badge.svg)](https://github.com/jav76/Cantus/actions/workflows/ci.yml)
[![Docs](https://github.com/jav76/Cantus/actions/workflows/docs.yml/badge.svg)](https://cantus.docs.jav26122.net)
[![Release](https://img.shields.io/github/v/release/jav76/Cantus?color=blue)](https://github.com/jav76/Cantus/releases)
[![Docker](https://img.shields.io/badge/GHCR-cantus-blue?logo=docker)](https://github.com/jav76/Cantus/pkgs/container/cantus)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Cantus is a self-hosted, real-time synchronized lyrics display platform. It connects with Spotify playback sessions to deliver sub-millisecond synchronized lyrics across web browsers, desktop operating systems, and dedicated kiosk displays.

Full documentation is hosted at [cantus.docs.jav26122.net](https://cantus.docs.jav26122.net).

---

## Features

- **Sub-Millisecond Synchronization**: Four-timestamp NTP clock offset estimation and smooth position interpolation filter network jitter.
- **Adaptive Polling**: Dynamically adjusts Spotify polling cadence (1.5s when playing, 5s when paused, 10s when idle) and suspends polling when zero viewers are connected to conserve API quota.
- **Multi-Tier Lyrics Resolution**: Direct local SQLite cache lookups with fallback to LRCLIB and negative caching for instrumental tracks.
- **Cross-Platform Client**: Uno Platform application supporting WebAssembly (modern web browsers and smart TVs) alongside native Linux, Windows, and macOS desktop targets.
- **Dynamic Theming**: Extracts complementary palettes and ambient gradients from active album artwork in real time.
- **Self-Hosted and Private**: Zero third-party telemetry. User tokens are encrypted at rest using ASP.NET Core Data Protection.

---

## Architecture

The system is structured following Clean Architecture principles across five solution projects:

| Layer | Project | Description |
| :--- | :--- | :--- |
| **Core Domain** | `src/Cantus.Core` | Domain models (`PlaybackState`, `LyricLine`), LRC parser, and engine contracts. |
| **Infrastructure** | `src/Cantus.Infrastructure` | Spotify PKCE authentication, LRCLIB integration, SQLite caching, and token encryption. |
| **Server Engine** | `src/Cantus.Server` | ASP.NET Core host, SignalR `PlaybackHub`, adaptive polling monitor, and REST APIs. |
| **Client Presentation** | `src/Cantus.Client` | Uno Platform application targeting WebAssembly and Skia desktop runtimes. |
| **Source Generators** | `src/Cantus.Generators` | Roslyn source generator for compile-time method tracing (`[TraceLog]`). |

---

## Prerequisites

### Containerized Deployment
- Docker Engine 24.0 or later
- Docker Compose v2.0 or later

### Local Development
- .NET 10 SDK (version 10.0.100 or later)
- Uno Platform WebAssembly workload:
  ```bash
  dotnet workload install wasm-tools
  ```
- Spotify Developer Account (for API client credentials)

---

## Quickstart with Docker

The fastest method to deploy Cantus is using Docker Compose.

### 1. Create Compose Configuration

Create a `docker-compose.yml` file:

```yaml
services:
  cantus:
    image: ghcr.io/jav76/cantus:latest
    container_name: cantus
    restart: unless-stopped
    ports:
      - "5000:5000"
    environment:
      - SPOTIFY_CLIENT_ID=your_spotify_client_id_here
      - CANTUS_HOST_URL=http://localhost:5000
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - cantus_data:/app/data

volumes:
  cantus_data:
    name: cantus_data
```

### 2. Start the Service

```bash
docker compose up -d
```

Access the web interface at `http://localhost:5000` (or your configured `CANTUS_HOST_URL`).

---

## Spotify Developer Setup

Cantus requires a registered Spotify Developer application to communicate with Spotify Web APIs.

1. Navigate to the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and log in.
2. Select **Create App** and specify an application name and description.
3. In **Redirect URIs**, add the callback endpoints for your instance:
   - Localhost: `http://localhost:5000/api/auth/spotify/callback` and `http://127.0.0.1:5000/api/auth/spotify/callback`
   - Production: `https://cantus.yourdomain.com/api/auth/spotify/callback`
4. Select **Web API** under the requested APIs.
5. Save the configuration and copy the generated **Client ID** into your environment configuration.

Because Cantus uses the OAuth 2.0 PKCE (Proof Key for Code Exchange) flow, a Client Secret is not required for client authentication.

---

## Configuration Reference

Cantus is configured via environment variables or `appsettings.json`:

| Variable | Required | Default | Description |
| :--- | :---: | :---: | :--- |
| `SPOTIFY_CLIENT_ID` | Yes | None | 32-character Client ID from your Spotify Developer Dashboard. |
| `CANTUS_HOST_URL` | Yes | `http://localhost:5000` | Canonical external URL used for OAuth redirects and SignalR handshakes. |
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | ASP.NET Core environment profile (`Production` or `Development`). |
| `DATA_DIR` | No | `/app/data` | Path to persistent storage for SQLite database and encryption keys. |
| `Lrclib__BaseUrl` | No | `https://lrclib.net` | Base endpoint for external LRCLIB lyrics lookups. |
| `CANTUS_LOG_CONFIGURATION` | No | `none` | Log verbosity profile: `none`, `debug`, or `trace`. |
| `PlaybackPoller__ActivePollIntervalMs` | No | `1500` | Spotify polling cadence (ms) during active playback. |

### Persistent Data Layout

Mount `/app/data` to persistent storage to preserve user sessions and cached lyrics:
- `/app/data/cantus.db`: SQLite database storing user sessions, track offsets, and cached LRC lyrics.
- `/app/data/keys/`: Cryptographic key ring used by ASP.NET Core Data Protection to encrypt tokens at rest.

---

## Building and Development

### Solution Build

Clone the repository and compile all projects:

```bash
git clone https://github.com/jav76/Cantus.git
cd Cantus
dotnet restore Cantus.slnx
dotnet build Cantus.slnx
```

### Running Test Suites

Execute all unit and integration test suites:

```bash
dotnet test Cantus.slnx
```

### Running the Server Locally

Start the ASP.NET Core backend server:

```bash
dotnet run --project src/Cantus.Server/Cantus.Server.csproj
```

### Running the Desktop Client

Launch the Uno Platform Skia desktop client:

```bash
dotnet run --project src/Cantus.Client/Cantus.Client/Cantus.Client.csproj -f net10.0-desktop
```

### Publishing WebAssembly Client

Compile the Uno Platform WebAssembly frontend:

```bash
dotnet publish src/Cantus.Client/Cantus.Client/Cantus.Client.csproj -f net10.0-browserwasm -c Release -o ./wasm_dist
```

### Packaging Linux AppImage

Build the standalone Linux AppImage package:

```bash
dotnet publish src/Cantus.Client/Cantus.Client/Cantus.Client.csproj -f net10.0-desktop -r linux-x64 -c Release -p:PublishSingleFile=true --self-contained true -o ./publish
./packaging/linux/build-appimage.sh ./publish ./artifacts 1.0.0
```

---

## Usage

1. Open Cantus in a web browser or launch the desktop client.
2. Select **Connect Spotify** to complete the one-time PKCE authorization flow.
3. Start playback on any Spotify device (phone, desktop, speaker, or web player).
4. Lyrics scroll automatically in synchronization with active playback.
5. Use manual offset adjustment controls (+50 ms / -50 ms) to calibrate track timing if necessary. Per-track offsets are persisted in the database.

---

## Documentation

Comprehensive guides, system architecture diagrams, and API specifications are available at:

[https://cantus.docs.jav26122.net](https://cantus.docs.jav26122.net)

- [User Guide](https://cantus.docs.jav26122.net/user-guide/)
- [Operator Guide & Self-Hosting](https://cantus.docs.jav26122.net/operator-guide/self-hosting/)
- [NTP Clock Synchronization Architecture](https://cantus.docs.jav26122.net/architecture/ntp-clock-sync/)
- [Adaptive Polling Engine Architecture](https://cantus.docs.jav26122.net/architecture/adaptive-polling/)
- [SignalR PlaybackHub Protocol Reference](https://cantus.docs.jav26122.net/reference/signalr-api/)
- [Contributing Guide](https://cantus.docs.jav26122.net/contributing/) (or see [CONTRIBUTING.md](CONTRIBUTING.md))

---

## Contributing

We welcome community contributions! Please review our [Contributing Guide](CONTRIBUTING.md) and [Development Documentation](https://cantus.docs.jav26122.net/contributing/) for details on getting started, coding standards, and submitting pull requests.

---

## License

This project is licensed under the terms of the [MIT License](LICENSE).
