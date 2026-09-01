# Self-Hosting with Docker

Cantus is designed to be completely zero-maintenance once deployed. This guide walks you through setting up a self-hosted instance using Docker Compose.

---

## 1. Quickstart with Docker Compose

Create a directory on your server or Raspberry Pi and add a `docker-compose.yml` file:

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
      - CANTUS_HOST_URL=https://cantus.yourdomain.com
      - ASPNETCORE_ENVIRONMENT=Production
    volumes:
      - cantus_data:/app/data

volumes:
  cantus_data:
    name: cantus_data
```

Start the container:

```bash
docker compose up -d
```

View the live logs:

```bash
docker compose logs -f cantus
```

Navigate to `http://<your-server-ip>:5000` (or your reverse-proxied HTTPS domain) in your browser.

---

## 2. Multi-Architecture Support

Cantus Docker images are published as multi-arch manifests supporting:
- `linux/amd64` (x86_64 servers, desktops, cloud VMs)
- `linux/arm64` (Raspberry Pi 4/5, Apple Silicon via Docker Desktop, ARM64 servers)

Docker will automatically pull the correct architecture for your host.

---

## 3. Persistent Data & Volume Layout

Cantus stores all state inside the `/app/data` directory within the container:

| Path | Purpose | Importance |
| :--- | :--- | :--- |
| `/app/data/cantus.db` | SQLite database storing user sessions, LRCLIB cache, and track latency calibrations. | Critical |
| `/app/data/keys/` | XML cryptographic key ring used by ASP.NET Core Data Protection to encrypt Spotify OAuth refresh tokens. | Critical |

!!! caution "Volume Persistence Required"
    Always mount a persistent volume (such as `cantus_data:/app/data` or a host bind mount `./data:/app/data`).
    If this volume is destroyed or lost, stored sessions will become unreadable and users will need to re-authenticate with Spotify.

---

## 4. Backup and Restore

### Creating a Backup

To back up your active Cantus database and encryption keys:

```bash
# Create a timestamped backup archive
docker compose exec cantus tar -czf /tmp/cantus-backup.tar.gz -C /app/data .
docker compose cp cantus:/tmp/cantus-backup.tar.gz ./cantus-backup-$(date +%Y%m%d).tar.gz
```

### Restoring from Backup

To restore on a new server:

```bash
# Start container once to initialize directory structure, then stop it
docker compose down
docker run --rm -v cantus_data:/app/data -v $(pwd):/backup alpine sh -c "tar -xzf /backup/cantus-backup-*.tar.gz -C /app/data"
docker compose up -d
```

---

## 5. Updating to the Latest Release

To update your Cantus container to the newest release:

```bash
docker compose pull
docker compose up -d --remove-orphans
```
