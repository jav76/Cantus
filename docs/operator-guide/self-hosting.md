# Self-Hosting Guide

Deploying Cantus in your home lab, Raspberry Pi, server, or cloud VM with Docker Compose.

---

## Architecture Overview

In a self-hosted environment, Cantus runs as a single lightweight multi-arch container (`linux/amd64` and `linux/arm64`) that serves both the ASP.NET Core API backend, SignalR WebSocket server, and static Uno WebAssembly frontend on port `5000`.

```mermaid
flowchart TD
    Internet([Internet / LAN]) -->|Port 80/443| ReverseProxy[Reverse Proxy / Caddy / Traefik / Nginx]
    ReverseProxy -->|Port 5000| Cantus[Cantus Container]
    Cantus --> DataVolume[(/app/data) SQLite DB & Encrypted Keys]
```

---

## 1. Docker Compose Setup

Create a `docker-compose.yml` file:

```yaml
version: '3.8'

services:
  cantus:
    image: ghcr.io/jav76/cantus:latest
    container_name: cantus
    restart: unless-stopped
    ports:
      - "5000:5000"
    environment:
      - SPOTIFY_CLIENT_ID=your_spotify_client_id_here
      - CANTUS_HOST_URL=https://lyrics.yourdomain.com
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - cantus_data:/app/data

volumes:
  cantus_data:
    name: cantus_data
```

---

## 2. Reverse Proxy Configuration

When hosting Cantus behind a reverse proxy with HTTPS, ensure WebSocket headers (`Upgrade` and `Connection`) and forwarded headers are passed.

### Caddyfile Example

```caddy
lyrics.yourdomain.com {
    reverse_proxy cantus:5000
}
```

### Nginx Example

```nginx
server {
    listen 80;
    server_name lyrics.yourdomain.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name lyrics.yourdomain.com;

    ssl_certificate /etc/letsencrypt/live/lyrics.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/lyrics.yourdomain.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## 3. Data Persistence & Security

The container stores persistent state in `/app/data`:
- `cantus.db`: SQLite database storing user sessions, positive/negative lyrics cache, and manual track latency calibration.
- `DataProtection-Keys/`: ASP.NET Core Data Protection XML encryption keys protecting OAuth refresh tokens at rest.

> [!CAUTION]
> Always mount a persistent volume at `/app/data`. If this directory is lost, active user sessions will be invalidated and users will need to re-authenticate with Spotify.
