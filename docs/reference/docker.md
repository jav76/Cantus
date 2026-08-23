# Docker Reference

Container image specifications, build targets, volume mounts, and security permissions.

---

## Container Image Details

- **Registry**: `ghcr.io/jav76/cantus`
- **Architectures**: Multi-arch support for `linux/amd64` and `linux/arm64` (Apple Silicon, Raspberry Pi 4/5).
- **Base Image**: `mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled` (distroless, minimal attack surface, non-root user).
- **Exposed Port**: `5000/tcp` (HTTP & WebSockets).

---

## Volume Mounts

| Container Path | Host Recommended | Purpose |
| :--- | :--- | :--- |
| `/app/data` | Docker named volume or host bind mount | Stores `cantus.db` SQLite database and ASP.NET Core Data Protection encryption keys. |

---

## Building Locally

To build the multi-stage Docker container locally:

```bash
docker build -t cantus:local .
```

To run the local image:

```bash
docker run -d \
  --name cantus \
  -p 5000:5000 \
  -e SPOTIFY_CLIENT_ID=your_client_id \
  -v cantus_data:/app/data \
  cantus:local
```
