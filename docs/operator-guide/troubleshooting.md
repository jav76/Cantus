# Troubleshooting & Diagnostics

This guide provides solutions to common setup, authentication, and network issues encountered when running Cantus.

---

## 1. Spotify Authentication Errors

### Error: `INVALID_CLIENT: Invalid client id`
- **Cause**: The `SPOTIFY_CLIENT_ID` environment variable is either unset, contains extra quotes, or is not registered in the Spotify Developer Dashboard.
- **Solution**: Verify that the 32-character Client ID in `.env` matches the Developer Dashboard exactly without leading/trailing whitespace.

### Error: `redirect_uri: Not matching configuration` or `INVALID_CLIENT: Invalid redirect URI`
- **Cause**: The callback URL that Cantus sent does not match the list of Redirect URIs in your Spotify Developer App settings.
- **Solution**:
  1. Check `CANTUS_HOST_URL` in your `.env` (e.g. `http://localhost:5000` or `https://cantus.yourdomain.com`).
  2. In the Spotify Developer Dashboard under **Settings -> Redirect URIs**, ensure `${CANTUS_HOST_URL}/api/auth/spotify/callback` (e.g. `http://localhost:5000/api/auth/spotify/callback` and `http://127.0.0.1:5000/api/auth/spotify/callback`) is added.
  3. Ensure there is no trailing slash on `CANTUS_HOST_URL` or redirect URI.

---

## 2. WebAssembly Client & Browser Caching

### Symptom: Frontend appears blank or does not reflect newly authorized accounts
- **Cause**: Browsers aggressively cache WebAssembly scripts, `.wasm` binaries, and application manifest files across deployments.
- **Solution**:
  1. Perform a hard refresh in your browser to bypass the cache:
     - **Windows / Linux**: <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>R</kbd> or <kbd>Ctrl</kbd> + <kbd>F5</kbd>
     - **macOS**: <kbd>Cmd</kbd> + <kbd>Shift</kbd> + <kbd>R</kbd>
  2. Open Developer Tools (<kbd>F12</kbd>), navigate to the **Application** / **Storage** tab, and click **Clear Site Data**.
  3. Ensure the backend container is running and healthy:
     ```bash
     curl -i http://localhost:5000/api/health
     ```

---

## 3. Real-Time WebSocket / SignalR Issues

### Symptom: Client shows "Reconnecting..." or drops every 60 seconds
- **Cause**: The reverse proxy is killing idle WebSocket connections or failing to send upgrade headers.
- **Solution**:
  1. Verify that your proxy config includes `Upgrade $http_upgrade` and `Connection "upgrade"`.
  2. Increase `proxy_read_timeout` and `proxy_send_timeout` to `300s` or higher.
  3. If using Cloudflare, verify that **WebSockets** is enabled in the Cloudflare Network dashboard.

---

## 4. Spotify API Rate Limiting (HTTP 429)

### Symptom: Logs show `Spotify rate limit exceeded (429)`
- **Cause**: Too many requests within a short timeframe.
- **Mitigation Built into Cantus**:
  - Cantus employs an **Adaptive Polling Engine** that automatically backs off to 5s intervals when playback is paused, 10s when idle, and completely halts polling when zero clients are connected to a room.
- **Operator Action**:
  - If sharing an instance among multiple active listeners, ensure non-active browser tabs are closed. Cantus will automatically spin down polling for disconnected rooms.

---

## 5. Clock Skew & Lyric Timing Drift

### Symptom: Lyrics appear ahead or behind audio on specific machines
- **Diagnostics**:
  - Press <kbd>D</kbd> on the client to open the Diagnostics HUD and check **NTP Clock Offset**.
  - If offset is `> 500ms`, the host OS clock on either the server or client is desynchronized.
- **Solution**:
  - Ensure `systemd-timesyncd` or `chrony` NTP service is running on the host server:
    ```bash
    timedatectl status
    ```

---

## 6. Health Checks & Server Logs

### Checking Endpoint Health

The backend exposes a health endpoint:
```bash
curl -i http://localhost:5000/api/health
```

Expected JSON response:
```json
{
  "status": "Healthy",
  "version": "1.0.0",
  "activeSessions": 1,
  "database": "Connected"
}
```

### Inspecting Container Logs

View live structured logs:
```bash
docker compose logs -f --tail=100 cantus
```
