# Reverse Proxy Configuration

When exposing Cantus to the internet or your local area network, running behind a reverse proxy provides automatic HTTPS certificates and ensures proper WebSocket connection upgrading for real-time SignalR streams.

---

## Reverse Proxy Requirements

Cantus relies on ASP.NET Core SignalR for real-time communication. Your reverse proxy must support:
- **WebSocket Upgrade Headers**: `Upgrade $http_upgrade; Connection "upgrade";`
- **Forwarded Headers**: `X-Forwarded-For`, `X-Forwarded-Proto`, and `Host`
- **Extended Timeouts**: Setting proxy read timeouts to at least `300s` prevents premature WebSocket disconnects during track pauses.

> **Trusting your proxy.** Cantus only honours `X-Forwarded-*` headers from proxies it trusts, and trusts **loopback only** by default. A proxy on the same host (the nginx example below, proxying `127.0.0.1:5000`) works unchanged. A proxy in another container or on another host must be trusted explicitly, or its forwarded scheme and client address are ignored — which typically shows up as OAuth callbacks being built with `http://` instead of `https://`:
>
> ```yaml
> # Trust a Docker bridge network
> - ForwardedHeaders__KnownNetworks__0=172.16.0.0/12
> # ...or a single proxy host
> - ForwardedHeaders__KnownProxies__0=10.0.0.5
> # ...or every proxy, ONLY when clients cannot reach Cantus directly
> - ForwardedHeaders__TrustAllProxies=true
> ```
>
> Prefer the narrowest option that works. `TrustAllProxies` lets any client that *can* reach Cantus directly spoof its own address and scheme, so pair it with removing the published port from `docker-compose.yml`.

---

## 1. Caddy (Recommended)

Caddy automatically obtains and renews Let's Encrypt certificates and passes WebSocket headers by default:

```caddy
# /etc/caddy/Caddyfile
cantus.yourdomain.com {
    reverse_proxy cantus:5000
}
```

If Cantus is running on the host machine directly:
```caddy
cantus.yourdomain.com {
    reverse_proxy 127.0.0.1:5000
}
```

---

## 2. Nginx

For standard Nginx installations, configure the server block with WebSocket forwarding:

```nginx
# /etc/nginx/sites-available/cantus
server {
    listen 80;
    server_name cantus.yourdomain.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name cantus.yourdomain.com;

    ssl_certificate /etc/letsencrypt/live/cantus.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/cantus.yourdomain.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;

        # WebSocket support for SignalR PlaybackHub
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";

        # Standard proxy headers
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Prevent WebSocket drop during idle playback
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
}
```

---

## 3. Traefik (Docker Compose)

If using Traefik v2 or v3, add labels to your Cantus service in `docker-compose.yml`:

```yaml
services:
  cantus:
    image: ghcr.io/jav76/cantus:latest
    container_name: cantus
    restart: unless-stopped
    environment:
      - SPOTIFY_CLIENT_ID=your_spotify_client_id_here
      - CANTUS_HOST_URL=https://cantus.yourdomain.com
    volumes:
      - cantus_data:/app/data
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.cantus.rule=Host(`cantus.yourdomain.com`)"
      - "traefik.http.routers.cantus.entrypoints=websecure"
      - "traefik.http.routers.cantus.tls.certresolver=myresolver"
      - "traefik.http.services.cantus.loadbalancer.server.port=5000"
```

---

## 4. Cloudflare Proxied Domains & SSL

If routing through Cloudflare CDN:
1. In the Cloudflare Dashboard, navigate to **Network** and ensure **WebSockets** is toggled **ON**.
2. Under **SSL/TLS**, set encryption mode to **Full (Strict)**.
3. Configure `CANTUS_HOST_URL=https://cantus.yourdomain.com` in your environment.
