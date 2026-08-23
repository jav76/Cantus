# Developer Quickstart

This guide walks you through building and running Cantus locally on your development workstation.

---

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher
- [Docker](https://docs.docker.com/get-docker/) & Docker Compose (optional, for containerized workflows)
- Spotify Free or Premium Account
- [Spotify Developer Application](../operator-guide/spotify-setup.md) Client ID

---

## 1. Clone the Repository

```bash
git clone https://github.com/jav76/Cantus.git
cd Cantus
```

---

## 2. Configure Environment

Copy the example environment file and add your Spotify Client ID:

```bash
cp .env.example .env
```

Edit `.env`:
```ini
SPOTIFY_CLIENT_ID=your_spotify_client_id_here
CANTUS_HOST_URL=http://localhost:5000
```

---

## 3. Run with .NET CLI

### Start the Server (ASP.NET Core 9 Minimal API)

```bash
dotnet run --project src/Cantus.Server/Cantus.Server.csproj
```

The server will start listening at `http://localhost:5000`.

### Run the Client (Desktop - Linux/Windows)

In a separate terminal:

```bash
dotnet run --project src/Cantus.Client/Cantus.Client/Cantus.Client.csproj -f net9.0-desktop
```

### Run the Client (WebAssembly / Browser)

```bash
dotnet run --project src/Cantus.Client/Cantus.Client/Cantus.Client.csproj -f net9.0-browserwasm
```

Navigate to `http://localhost:5001` to view the lyrics display in your web browser.

---

## 4. Run with Docker Compose

Alternatively, start both the backend server and embedded WebAssembly client in a single container:

```bash
docker compose up -d
```

Open `http://localhost:5000` to complete Spotify PKCE authorization and start synchronized playback.

---

## 5. Running Automated Tests

Run the full unit and integration test suite:

```bash
dotnet test Cantus.slnx
```
