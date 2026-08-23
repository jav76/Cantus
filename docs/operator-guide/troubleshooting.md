# Troubleshooting & Diagnostics

Common issues, diagnostic checklists, and resolution steps for Cantus operators.

---

## Diagnostics HUD

The Cantus desktop and web clients feature a built-in **Diagnostics HUD**:

- Press <kbd>D</kbd> or <kbd>F2</kbd> in the client interface to toggle the Diagnostics overlay.
- Inspect real-time metrics:
  - **RTT (Round Trip Time)**: Network latency between client and Cantus server.
  - **Clock Skew (Offset)**: Computed offset between client time and server time.
  - **Active Poll Rate**: Current server polling cadence (`500ms`, `3000ms`, `10000ms`, or `Idle`).
  - **Lyrics Source**: Indicates whether lyrics were retrieved from `Cache (SQLite)` or `LRCLIB`.

---

## Common Issues & Fixes

### 1. "Invalid redirect_uri" Error during Spotify Login

**Symptom**: When attempting to sign in with Spotify, you receive `INVALID_CLIENT: Invalid redirect URI`.

**Cause**: The URL requested by Cantus does not match the list of Redirect URIs in your Spotify Developer App.

**Fix**:
1. Check `CANTUS_HOST_URL` in your environment or reverse proxy.
2. Ensure `https://<your-domain>/api/auth/callback` is added word-for-word in the Spotify Developer Dashboard.

---

### 2. Lyrics Desynchronization or Lag

**Symptom**: Lyrics appear slightly before or after vocals.

**Fix**:
- Use the keyboard shortcuts <kbd>[</kbd> and <kbd>]</kbd> (or <kbd>-</kbd> and <kbd>+</kbd>) to calibrate per-track offsets in **±50ms** increments.
- The offset is automatically saved to SQLite and applied whenever that track plays in the future.

---

### 3. Server Logs Inspection

View container logs using Docker:

```bash
docker logs -f cantus
```

To enable verbose debug logging, set the environment variable:

```ini
Logging__LogLevel__Default=Debug
```
