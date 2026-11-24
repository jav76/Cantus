# Reverse Proxy Configuration

When exposing Cantus to the internet or your local area network, running behind a reverse proxy provides automatic HTTPS certificates and ensures proper WebSocket connection upgrading for real-time SignalR streams.

---

## Reverse Proxy Requirements

Cantus relies on ASP.NET Core SignalR for real-time communication. Your reverse proxy must support:

- **WebSocket Upgrade Headers**: `Upgrade $http_upgrade; Connection "upgrade";`
- **Forwarded Headers**: `X-Forwarded-For`, `X-Forwarded-Proto`, and `Host` (or `X-Forwarded-Host`, which Cantus also honours from a trusted proxy)
- **Idle Timeouts above 30 seconds**: the server sends a SignalR keep-alive every 15 seconds and clients give up after 30 seconds of silence, so any proxy idle timeout above 30 seconds is enough. The `300s` in the examples leaves plenty of headroom.
- **Compression is optional**: Cantus already sends the web client's precompressed brotli or gzip files (about 7 MB instead of 30 MB). Proxy compression still helps API responses but is not needed for the client.

> **Trusting your proxy.** Cantus only honours `X-Forwarded-*` headers from proxies it trusts, and trusts **loopback only** by default. A proxy on the same host (the nginx example below, proxying `127.0.0.1:5000`) works unchanged. A proxy in another container or on another host must be trusted explicitly, or its forwarded scheme and client address are ignored. This typically shows up as OAuth callbacks being built with `http://` instead of `https://`:
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

## Display Pairing Behind a Reverse Proxy

[Pairing a Display](displays.md) needs no extra proxy rules. It uses the same `/api` routes and `/hubs/playback` WebSocket as the rest of the app. Three things depend on your proxy setup:

- **The pairing rate limit keys on the client address.** Requesting a code and approving one are each limited per client address (`DisplayPairing__RateLimitPermitLimit`, 10 per minute by default). Cantus takes that address from `X-Forwarded-For` only when the proxy is trusted (see above). If it is not, every client appears to come from the proxy's own address and shares one limit, so screens show "Too many pairing requests" and approvals fail with `429` as soon as a few people pair at once. Trust the proxy instead of raising the limit.
- **`TrustAllProxies` lets clients pick their own address.** If Cantus is also reachable directly, a client can send a made-up `X-Forwarded-For` and get a fresh rate limit for each value. Remove the published port when you use it.
- **Screens behind one NAT share a limit.** Every device on one home network usually has the same public address. Raise `DisplayPairing__RateLimitPermitLimit` if many screens on one network pair at the same time.

The QR code on a waiting screen encodes the address the screen loaded Cantus from. Open screens at the public address phones use (for example `https://cantus.yourdomain.com`), not an internal one such as `http://cantus:5000`.

## Local Sessions Behind a Reverse Proxy

[Local Sessions](self-hosting.md#6-let-people-use-their-own-players) (`LocalSessions__Enabled`) need no extra proxy rules either. Creating one is also limited per client address (`LocalSessions__RateLimitPermitLimit`, 5 per 10 minutes by default), so the same rule applies: trust the proxy, or every desktop app shares the proxy's limit and gets `429` once a few people start Local Sessions. On an internet-facing server, also set `LocalSessions__JoinCode`.

!!! warning "Credentials in access logs"
    A browser cannot set headers on a WebSocket, so the web app's SignalR client puts the connection's credential (a Session id or a Display token) in the `access_token` query parameter of `/hubs/playback`. A proxy that logs full request URLs writes these credentials to its access log. Drop query strings from logged URLs, or keep the logs as private as the database.

---

## 1. Caddy (Recommended)

Caddy automatically obtains and renews Let's Encrypt certificates and passes WebSocket headers by default. When Caddy runs in another container (`cantus:5000`), trust its network with `ForwardedHeaders__KnownNetworks__0` (see above):

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
      - Spotify__ClientId=your_spotify_client_id_here
      - CANTUS_HOST_URL=https://cantus.yourdomain.com
      # Traefik runs in another container, so trust its network
      - ForwardedHeaders__KnownNetworks__0=172.16.0.0/12
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
