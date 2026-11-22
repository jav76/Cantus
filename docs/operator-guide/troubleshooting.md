# Troubleshooting & Diagnostics

This guide provides solutions to common setup, authentication, and network issues encountered when running Cantus.

---

## 1. Spotify Authentication Errors

### Error: `INVALID_CLIENT: Invalid client id`
- **Cause**: The `Spotify__ClientId` environment variable is either unset, contains extra quotes, or is not registered in the Spotify Developer Dashboard.
- **Solution**: Verify that the 32-character Client ID in `.env` matches the Developer Dashboard exactly without leading/trailing whitespace.

### Error: `redirect_uri: Not matching configuration` or `INVALID_CLIENT: Invalid redirect URI`
- **Cause**: The callback URL that Cantus sent does not match the list of Redirect URIs in your Spotify Developer App settings.
- **Solution**:
    1. Check `CANTUS_HOST_URL` (e.g. `https://cantus.yourdomain.com`). The repository's `docker-compose.yml` defaults it to `http://localhost:5000`, which Spotify rejects.
    2. In the Spotify Developer Dashboard under **Settings -> Redirect URIs**, ensure `${CANTUS_HOST_URL}/api/auth/spotify/callback` is added exactly.
    3. When you open Cantus at `http://localhost:5000`, it sends a `localhost` callback whatever `CANTUS_HOST_URL` says, and Spotify rejects `localhost`. Open `http://127.0.0.1:5000` instead and register `http://127.0.0.1:5000/api/auth/spotify/callback`.
    4. Behind a proxy with `CANTUS_HOST_URL` unset, an `http://` callback means the proxy is not trusted. See [Reverse Proxy](reverse-proxy.md).

### Sign-in failure pages

When a sign-in fails, Cantus shows a page with one of these titles. The server log has the full Spotify error.

| Page title | Cause | Fix |
| :--- | :--- | :--- |
| Your account is not on this app's user list | The Spotify app is in Development Mode and this account was not added. | Add the account under **User Management** in the app, or let the user [sign in with their own app](spotify-setup.md#5-users-own-spotify-apps). |
| The Spotify app owner needs Premium | The account that owns the Spotify app has no Premium. | Create the app with a Premium account. |
| Spotify did not accept this Client ID | The Client ID is malformed or no such app exists. | Copy the Client ID again from the app's **Settings** page. |
| Spotify rejected the sign-in | Usually the redirect URI in the app differs from the one Cantus sent. | Register the URI shown on the page exactly. |
| Spotify is not set up on this server | No `Spotify__ClientId`, and no own app was chosen. | Set `Spotify__ClientId`, or sign in with your own app. |
| Own Spotify apps are turned off here | A Client ID was supplied but `Spotify__AllowUserClientIds` does not allow it. | See [Users' own Spotify apps](spotify-setup.md#5-users-own-spotify-apps). |
| Sign-in expired | The sign-in took over 10 minutes, or finished in another browser, so the PKCE cookies were missing. | Start again from Cantus in the same browser. |
| Sign-in cancelled | The consent screen was declined. | Nothing to fix. |
| Spotify sign-in failed | Spotify returned an error Cantus does not recognise. | Try again, then check the server log for the Spotify error. |

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

### Symptom: Client shows "Reconnecting..." or drops repeatedly
- **Cause**: The reverse proxy is killing idle WebSocket connections or failing to send upgrade headers.
- **Solution**:
    1. Verify that your proxy config includes `Upgrade $http_upgrade` and `Connection "upgrade"`.
    2. Make sure the proxy idle timeout (`proxy_read_timeout` and `proxy_send_timeout` in nginx) is above 30 seconds. The server sends a keep-alive every 15 seconds.
    3. If using Cloudflare, verify that **WebSockets** is enabled in the Cloudflare Network dashboard.

---

## 4. Spotify API Rate Limiting (HTTP 429)

### Symptom: Logs show `Spotify API rate limit hit for Spotify user ...` or `Spotify rate limit hit. Retry after ...`
- **Cause**: Too many requests within a short timeframe.
- **Mitigation Built into Cantus**:
    - On a `429`, Cantus pauses Spotify polling for every owner (the limit applies to the whole Client ID) until Spotify's `Retry-After` time (60 seconds if none is given).
    - The adaptive poller backs off when paused (5s, then 15s after 1 minute, 30s after 5 minutes) and when idle (10s, then 30s after 2 minutes, 60s after 10 minutes), slows to 20s while playing when every viewer is hidden (30s paused, 60s idle), and stops polling an owner with no connected viewers. Intervals are set under [`PlaybackPoller`](../reference/configuration.md#adaptive-playback-polling-playbackpoller).
- **Operator Action**:
    - If sharing an instance among multiple active listeners, ensure non-active browser tabs are closed. Cantus will automatically spin down polling for owners with no connected viewers. A paired Display counts as a viewer, so remove Displays nobody watches.

---

## 5. Clock Skew & Lyric Timing Drift

### Symptom: Lyrics appear ahead or behind audio on specific machines
- **Diagnostics**:
    - Open the Diagnostics HUD from the **Diagnostics** button in the client's header bar (or the **Diagnostics HUD** menu item) and check **RTT / NTP Skew**.
    - The client corrects for the measured skew, but a skew of several hundred milliseconds or more suggests the server or client OS clock is not synchronized.
    - If the clocks are fine, adjust the per-track offset instead (see [Timing & Latency Calibration](../user-guide/timing-and-calibration.md)).
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

Expected JSON response (`200`). If the database cannot be reached, `status` is `Degraded`, `database` is `Disconnected` and the code is `503`:
```json
{
  "status": "Healthy",
  "version": "1.0.0",
  "activeSessions": 1,
  "database": "Connected",
  "commitSha": "..."
}
```

### Inspecting Container Logs

View live logs:
```bash
docker compose logs -f --tail=100 cantus
```

The default `none` profile logs `Information` and above. For more detail, set `CANTUS_LOG_CONFIGURATION=debug` (or `trace`) and recreate the container. This also writes `/tmp/cantus/logs/cantus-server.log` inside the container and a `LogEntries` table in the database. See [Logging](../reference/configuration.md#logging-configurations-cli-parameters).

### Session ids in logs from older releases

A Session id is the value of the `cantus_session_id` cookie, so anyone who reads it can act as that owner. The server now logs a Session only as `{SessionRef}`, a 12-character hash that cannot be turned back into the id, and logs the Spotify user id where a message is about the Spotify account. Display tokens are never logged.

Logs written by earlier releases may hold full Session ids, under `{UserId}` or `User:`. That includes rows in the `LogEntries` table (written with `--log-configuration debug` or `trace`) and the rolling `cantus-*.log` files. To clear them after upgrading:

```bash
docker compose stop cantus
# cantus_data is the volume from the self-hosting guide. The repository's own
# docker-compose.yml names it cantus-data. Check with `docker volume ls`.
docker run --rm -v cantus_data:/data alpine \
  sh -c "apk add -q sqlite && sqlite3 /data/cantus.db 'DELETE FROM LogEntries; VACUUM;'"
docker compose up -d --force-recreate cantus
```

Recreating the container also removes the rolling log files, which live in its temp directory (`/tmp/cantus/logs`). Copies already shipped to an external log store have to be removed there. If old logs may have leaked, have the owner sign out: that deletes the Session, so its id stops working, and signing in again creates a new one. Signing out also removes the owner's Displays, which then need pairing again.

---

## 7. Paired Displays

### Symptom: Screens show "Too many pairing requests" or approvals fail with `429`
- **Cause**: The pairing rate limit is per client address. Behind an untrusted proxy every client shares the proxy's address, and so one limit.
- **Fix**: Trust the proxy with `ForwardedHeaders__KnownProxies__0` or `ForwardedHeaders__KnownNetworks__0`. See [Display pairing behind a reverse proxy](reverse-proxy.md#display-pairing-behind-a-reverse-proxy).

### Symptom: A phone cannot open the QR code from a waiting screen
- **Cause**: The QR encodes the address the screen loaded Cantus from. An internal address such as `http://cantus:5000`, or `127.0.0.1` in the desktop app, does not work from a phone.
- **Fix**: Open the screen at the public address, or start the desktop app with `--server-url`. See [Pairing & Managing Displays](../user-guide/displays.md#desktop-app-as-a-display).

### Symptom: A Display went back to the pairing screen on its own
- **Cause**: The owner removed it, or logged out (which removes all of their Displays). A server restart alone does not unpair a screen: it retries before giving up.
- **Fix**: Pair the screen again. To tell the two apart, check the logs: a removal logs `Display {DisplayId} revoked`, while a log out logs only `Closed {Count} connection(s) of revoked Display {DisplayId}` (and only if the screen was connected).
