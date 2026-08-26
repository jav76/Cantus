# REST API Reference

In addition to the real-time SignalR hub, Cantus provides ASP.NET Core Minimal API HTTP endpoints for authentication, session management, and system health checks.

---

## Authentication Endpoints

### `GET /api/auth/login`
Initiates the Spotify OAuth 2.0 PKCE authentication flow.

- **Query Parameters**: None.
- **Response**: `302 Found` redirect to Spotify's `https://accounts.spotify.com/authorize` with generated `code_challenge`, `client_id`, and `user-read-playback-state` scopes.

---

### `GET /api/auth/callback`
Handles the redirect callback from Spotify following user consent.

- **Query Parameters**:
  - `code` (`string`): Spotify authorization code.
  - `state` (`string`): Anti-forgery validation state.
- **Behavior**:
  1. Validates PKCE challenge and exchanges code for access & refresh tokens.
  2. Encrypts tokens via ASP.NET Core Data Protection.
  3. Saves user profile in SQLite database.
- **Response**: `302 Found` redirect to `/` (Client Home).

---

### `GET /api/auth/session`
Returns current authenticated session information for the calling browser.

- **Response `200 OK`**:
```json
{
  "isAuthenticated": true,
  "userId": "spotify_user_12345",
  "displayName": "Jane Doe",
  "avatarUrl": "https://i.scdn.co/image/...",
  "sessionExpiresAtUtc": "2026-08-26T04:30:00Z"
}
```

---

## Health & Monitoring Endpoints

### `GET /api/health`
System liveness and readiness probe for Docker / Kubernetes orchestrators.

- **Response `200 OK`**:
```json
{
  "status": "Healthy",
  "version": "1.0.0",
  "activeSessions": 1,
  "database": "Connected"
}
```

---

### `GET /api/diagnostics`
Provides runtime diagnostics, caching statistics, and active poller states.

- **Response `200 OK`**:
```json
{
  "uptimeSeconds": 86400,
  "activePollingWorkers": 1,
  "cachedLyricsCount": 342,
  "negativeCacheCount": 18,
  "totalMemoryAllocatedMb": 48.2
}
```
