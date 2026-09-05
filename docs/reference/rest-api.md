# REST API Reference

In addition to the real-time SignalR hub, Cantus provides ASP.NET Core Minimal API HTTP endpoints for authentication, session management, and system health checks.

---

## Authentication Endpoints

### `GET /api/auth/spotify/login` (or `GET /api/auth/login`)
Initiates the Spotify OAuth 2.0 PKCE authentication flow.

- **Query Parameters**:
  - `json` (`bool`, optional): If `true`, returns `{ "authorizationUrl": "..." }` in JSON instead of 302 redirect.
  - `client_id` (`string`, optional): Ephemeral client handshake ID. When provided, the server notifies this specific client via SignalR (`ReceiveAuthSession` on group `client_{clientId}`) upon successful OAuth callback, allowing seamless desktop and remote display login.
- **Response**: `302 Found` redirect to Spotify's `https://accounts.spotify.com/authorize` with generated `code_challenge`, `client_id`, and requested scopes.

---

### `GET /api/auth/spotify/callback` (or `GET /api/auth/callback`)
Handles the redirect callback from Spotify following user consent.

- **Query Parameters**:
  - `code` (`string`): Spotify authorization code.
  - `state` (`string`): Anti-forgery validation state.
- **Behavior**:
  1. Validates PKCE challenge and exchanges code for access & refresh tokens.
  2. Encrypts tokens via ASP.NET Core Data Protection.
  3. Saves user session in SQLite database and updates active session registry.
  4. Broadcasts session list update to all connected SignalR clients (including desktop app).
- **Response**: `200 OK` HTML landing page with handoff to desktop client or web player.

---

### `GET /api/auth/sessions`
Returns all currently authorized Spotify user account sessions stored on the server.

- **Response `200 OK`**:
```json
[
  {
    "userId": "spotify_user_12345",
    "displayName": "Jane Doe",
    "avatarUrl": "https://i.scdn.co/image/...",
    "expiresAtUtc": "2026-08-26T04:30:00Z"
  }
]
```

---

### `GET /api/auth/me`
Returns current authenticated session information for the calling browser or client.

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

### `DELETE /api/auth/sessions/{userId}`
Revokes an authorized Spotify account session and removes its encrypted tokens from SQLite.

- **Response `204 No Content`**

---

### `POST /api/auth/logout`
Clears current user session cookies and revokes the active session.

- **Response `200 OK`**: `{ "message": "Logged out successfully" }`

---

## Lyrics & Playback Endpoints

### `GET /api/lyrics/{trackId}`
Retrieves cached synchronized lyrics for a specific Spotify track ID.

- **Response `200 OK`**:
```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "isInstrumental": false,
  "lines": [
    {
      "startTimeMs": 18450,
      "endTimeMs": 22300,
      "text": "We're no strangers to love"
    }
  ]
}
```

---

### `POST /api/lyrics/offset`
Saves a manual timing offset adjustment for a track.

- **Request Body**:
```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "offsetMs": 150
}
```
- **Response `200 OK`**

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
