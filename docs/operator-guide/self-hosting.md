# Self-Hosting with Docker

Cantus is designed to be completely zero-maintenance once deployed. This guide walks you through setting up a self-hosted instance using Docker Compose.

---

## 1. Quickstart with Docker Compose

Create a directory on your server or Raspberry Pi and add a `docker-compose.yml` file:

```yaml
services:
  cantus:
    image: ghcr.io/jav76/cantus:latest
    container_name: cantus
    restart: unless-stopped
    ports:
      - "5000:5000"
    environment:
      - Spotify__ClientId=your_spotify_client_id_here
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

Navigate to your HTTPS domain, or `http://127.0.0.1:5000` on the Docker host. The page also loads at `http://<your-server-ip>:5000`, but Spotify login only works over HTTPS or a loopback address (see [Spotify Developer App Setup](spotify-setup.md#2-configure-redirect-uris)).

`ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:5000` and `DATA_DIR=/app/data` are already set in the image. All other settings are in the [Configuration Reference](../reference/configuration.md).

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
| `/app/data/cantus.db` | SQLite database: Sessions, Displays, lyrics cache, lyrics overrides and per-track offsets. | Critical |
| `/app/data/keys/` | XML key ring used by ASP.NET Core Data Protection to encrypt Spotify OAuth tokens. | Critical |

!!! caution "Volume Persistence Required"
    Always mount a persistent volume (such as `cantus_data:/app/data` or a host bind mount `./data:/app/data`).
    If the key ring is lost, stored Spotify tokens become unreadable and users must log in with Spotify again. If the database is lost, Sessions and Displays are gone and every screen must be paired again.

---

## 4. Backup and Restore

### Creating a Backup

To back up your active Cantus database and encryption keys:

```bash
# Create a timestamped backup archive
docker compose exec cantus tar -czf /tmp/cantus-backup.tar.gz -C /app/data .
docker compose cp cantus:/tmp/cantus-backup.tar.gz ./cantus-backup-$(date +%Y%m%d).tar.gz
```

### Automatic Copies Before Upgrades

When a new version changes the database schema, Cantus copies the existing database before migrating it, as `cantus.db.pre-<migration>.bak` in the same folder (`/app/data` in Docker). The 3 newest copies are kept. If an upgrade is interrupted, stop the container, delete `cantus.db-wal` and `cantus.db-shm` if present, replace `cantus.db` with the newest copy and start again. The copy does not include the encryption keys, so keep making full backups as shown above. Set `Database__BackupBeforeMigrations=false` to turn this off.

### Restoring from Backup

To restore on a new server:

```bash
# Stop the container so the database is not in use
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

---

## 6. Let People Use Their Own Players

The Linux desktop app can show lyrics for what a media player on the user's computer plays, with no Spotify account (see [Use this computer's players](../user-guide/local-players.md)). This creates a Local Session on your server. It is off by default:

```yaml
    environment:
      - LocalSessions__Enabled=true
      - LocalSessions__JoinCode=choose-a-long-code
```

!!! warning "Set a join code on internet-facing servers"
    Without `LocalSessions__JoinCode`, anyone who can reach the server can create a Local Session and start lyrics lookups, with no Spotify allowlist in the way. A join code (at least 8 characters) limits that to people you give it to. Creation is also rate limited per client address, so behind a reverse proxy configure [forwarded headers](../reference/configuration.md#reverse-proxy-trust-forwardedheaders), or every user shares the proxy's limit.

Local Sessions unused for `LocalSessions__IdleExpiryDays` (90 days) are deleted with their Displays the next time someone creates a Local Session. Turning `LocalSessions__Enabled` off again stops new Local Sessions and stops existing ones from sending playback; they can still sign out, and none expire while it stays off. All settings are in the [Configuration Reference](../reference/configuration.md#local-sessions-localsessions).
