# REST API Reference

In addition to the real-time SignalR hub, Cantus provides ASP.NET Core Minimal API HTTP endpoints for authentication, session management, and system health checks.

---

## Authentication Endpoints

### Session credential
Endpoints that act on the caller's Session read its credential from, in order: the `cantus_session_id` cookie, the `access_token`, `token` or `session_id` query parameter, or the `Authorization` header (`Bearer <id>` or the raw id).

The credential is the Session id issued by the OAuth callback. The server matches it against that exact id only. A Spotify user id is not a credential: `/me` and `POST /api/lyrics/offset` return `401`, `/sessions` returns an empty list, and revoke or logout leaves every Session untouched.

### Display token
A Display (a paired screen) holds a view-only Display token instead of a Session. It presents the token in the `cantus_display_token` cookie, the `display_token` query parameter, or the shared `access_token` query parameter or `Authorization: Bearer` header. When a request carries both a valid Session and a Display token, the Session wins. When no slot holds a valid Session, every slot is tried as a Display token, so a stale `cantus_session_id` cookie does not hide a Display token sent in `access_token` or `Authorization`.

The server stores only a SHA-256 hash of each token. A revoked or unknown token is treated as no credential.

A Display token is rejected with `403 Forbidden` by every owner-only endpoint: `GET /api/auth/me`, `GET /api/auth/sessions`, `DELETE /api/auth/sessions/{userId}`, `POST /api/auth/logout`, `GET /api/lyrics/{trackId}/candidates`, `POST /api/lyrics/{trackId}/override`, `POST /api/lyrics/offset`, and every [Display endpoint](#display-endpoints) except `POST /api/displays/pairings`. Nothing is changed or revoked.

---

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
Revokes an authorized Spotify account session and removes its encrypted tokens from SQLite. `{userId}` must be the caller's own Session id, matching the [session credential](#session-credential).

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
- Requires the caller's [session credential](#session-credential).
- **Response `200 OK`**
- **Response `401 Unauthorized`**: no credential, or the credential does not match a Session id.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token).

---

## Display Endpoints

A Display is paired in three steps:

1. The screen opens an anonymous [PlaybackHub](signalr-api.md) connection and calls `POST /api/displays/pairings` with its connection id. It shows the returned code.
2. The owner enters the code on a signed-in device, which calls `POST /api/displays/pairings/approve` with a name for the Display.
3. The server sends the new Display token to the screen's connection as [`ReceiveDisplayPaired`](signalr-api.md#receivedisplaypaired). The screen stores the token and reconnects with it.

Pending pairings live in server memory, so a restart drops them. Codes are 8 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no `0`, `O`, `1`, `I` or `L`), single-use, and expire after 5 minutes. A connection has at most one pending code: a new request replaces the old one, and disconnecting cancels it. Lengths, lifetimes and limits are set under [`DisplayPairing`](configuration.md#display-pairing-displaypairing).

Both pairing endpoints are rate limited per client address, each counted on its own: 10 requests per 60 seconds by default. IPv6 addresses in the same `/64` count as one client, and changing the case of the path or adding a trailing slash does not reset the limit. Over the limit they return `429 Too Many Requests`. Behind a reverse proxy, configure [`ForwardedHeaders`](configuration.md#reverse-proxy-trust-forwardedheaders) so the limit sees real client addresses.

---

### `POST /api/displays/pairings`
Starts a pairing. No credential needed.

- **Request Body**:
```json
{ "connectionId": "<PlaybackHub connection id>" }
```
- **Response `200 OK`**:
```json
{
  "pairingId": "3f1c9a0e5b7d4e2a9c6b8d0f1e2a3b4c",
  "code": "K7QH2MZP",
  "expiresAtUtc": "2026-11-20T18:05:00Z"
}
```
- **Response `400 Bad Request`**: `connectionId` is missing or is not a live PlaybackHub connection. Call this only after the connection has started.
- **Response `429 Too Many Requests`**: rate limit reached.
- **Response `503 Service Unavailable`**: too many pairings are waiting for approval (`DisplayPairing:MaxPendingPairings`).

---

### `POST /api/displays/pairings/approve`
Approves a code and names the new Display. Requires the caller's [session credential](#session-credential).

- **Request Body**:
```json
{ "code": "K7QH-2MZP", "name": "Living room TV" }
```
  `code` ignores case, spaces and hyphens. `name` is trimmed and must be 1 to 64 characters.
- **Response `201 Created`**: the new Display, with `Location: /api/displays/{displayId}`. The token is not in this response; it goes only to the waiting connection.
```json
{
  "id": "9b2e4c6a8d0f4b1e93a5c7e9f1b3d5a7",
  "name": "Living room TV",
  "createdAtUtc": "2026-11-20T18:01:12Z",
  "lastSeenAtUtc": null
}
```
- **Response `400 Bad Request`**: the name is empty or too long. The code stays valid.
- **Response `401 Unauthorized`**: no Session credential. The code stays valid.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token). The code stays valid.
- **Response `404 Not Found`**: the code is unknown, already used, expired, or its connection has disconnected. If the connection drops while the approval is running, the new Display is removed and the code is used up, so the screen must request a new code.
- **Response `429 Too Many Requests`**: rate limit reached.

---

### `GET /api/displays`
Lists the caller's Displays, oldest first. Requires the caller's [session credential](#session-credential).

- **Response `200 OK`**: an array of Displays, shaped like the approve response. Neither the Display token nor the owner's Session id is included.
- **Response `401 Unauthorized`** / **`403 Forbidden`**: as for approve.

---

### `PATCH /api/displays/{displayId}`
Renames one of the caller's Displays. Requires the caller's [session credential](#session-credential).

- **Request Body**:
```json
{ "name": "Bedroom" }
```
- **Response `204 No Content`**
- **Response `400 Bad Request`**: the name is empty or longer than 64 characters.
- **Response `401 Unauthorized`** / **`403 Forbidden`**: as for approve.
- **Response `404 Not Found`**: the caller has no Display with that id.

---

### `DELETE /api/displays/{displayId}`
Revokes one of the caller's Displays. Requires the caller's [session credential](#session-credential).

Its token stops working at once. Each of its live hub connections receives [`ReceiveDisplayRevoked`](signalr-api.md#receivedisplayrevoked) and is then closed. The owner's Session, its connections and the owner's other Displays are untouched.

- **Response `204 No Content`**
- **Response `401 Unauthorized`** / **`403 Forbidden`**: as for approve.
- **Response `404 Not Found`**: the caller has no Display with that id.

---

### `GET /api/lyrics/{trackId}/candidates`
Lists the lyrics every enabled provider has for the owner's current track, so a wrong automatic match can be replaced.

- **Auth**: requires a Session (cookie, `Authorization: Bearer <session id>`, or `access_token` query parameter).
- **Path Parameters**:
  - `trackId` (`string`): Spotify track ID. Must be the track currently playing for the calling Session, since providers search by its title, artist and duration.
- **Behavior**:
  1. Queries LRCLIB and, when `Netease:Enabled` is `true`, NetEase in parallel. Nothing is cached or changed.
  2. LRCLIB returns every search result that has synced lyrics, plain lyrics, or an instrumental flag.
  3. NetEase returns synced lyrics or an instrumental marker (`isSynced: false`, `isInstrumental: true`), from the first 5 search results by a matching artist. Duration does not filter candidates.
  4. A provider that fails (network error, timeout, 5xx, NetEase payload error code) is listed in `unavailableProviders` and the rest of the list is still returned. If only some NetEase lyric lookups fail, the songs that succeeded are still returned; NetEase is listed as unavailable only when its search fails or failed lookups leave it with no candidates.
- **Response `200 OK`**:
```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "candidates": [
    {
      "provider": "LRCLIB",
      "providerCandidateId": "3396226",
      "title": "Never Gonna Give You Up",
      "artist": "Rick Astley",
      "album": "Whenever You Need Somebody",
      "durationMs": 213000,
      "isSynced": true,
      "isInstrumental": false,
      "preview": [
        "We're no strangers to love",
        "You know the rules and so do I"
      ]
    }
  ],
  "unavailableProviders": ["NetEase"]
}
```
- `preview` holds the first non-blank lyric lines, 2 at most. It is empty for an instrumental candidate.
- **Response `401 Unauthorized`**: no Session credential, or the Session is unknown.
- **Response `403 Forbidden`**: the caller holds a Display token. No provider is queried.
- **Response `404 Not Found`**: `trackId` is not the calling Session's current track, or nothing is playing.

---

### `POST /api/lyrics/{trackId}/override`
Corrects the lyrics for the owner's current track. An override is stored per track and applies server-wide, like track offsets. It survives restarts, cache expiry and negative caching until it is cleared.

- **Auth**: requires a Session (cookie, `Authorization: Bearer <session id>`, or `access_token` query parameter).
- **Path Parameters**:
  - `trackId` (`string`): Spotify track ID. Must be the track currently playing for the calling Session.
- **Request Body**:
```json
{
  "action": "candidate",
  "provider": "LRCLIB",
  "candidateId": "3396226"
}
```
- **Actions**:

| `action` | Effect |
| :--- | :--- |
| `candidate` | Loads the candidate's lyrics from `provider` (as listed by `GET /api/lyrics/{trackId}/candidates`) and stores them as the override. `provider` and `candidateId` are required. |
| `noLyrics` | Stores a "no lyrics" override. Viewers see an empty lyrics view and no provider is queried for the track. |
| `refetch` | Queries the providers again, skipping the caches. A definitive answer replaces the cached automatic match (including a "not found" entry) and removes any override. If a provider cannot be reached, nothing is changed. |
| `clear` | Removes any override and returns the track to automatic matching, served from the cache when it still holds a match. |

- **Behavior**: after a successful action, every Session currently on the track (not only the caller) and its Displays receive the new lyrics through the hub's `ReceiveLyrics`. A `noLyrics` result, or a re-fetch that finds nothing, is sent as an empty lyrics payload.
- **Response `200 OK`**: the lyrics now in effect, in the same shape as `GET /api/lyrics/{trackId}` (`lines` is empty when the track has no lyrics).
- **Response `400 Bad Request`**: unknown `action`, `candidate` without `provider` or `candidateId`, or `provider` is not an enabled provider.
- **Response `401 Unauthorized`**: no Session credential, or the Session is unknown.
- **Response `403 Forbidden`**: the caller holds a Display token. Nothing is changed and nothing is broadcast.
- **Response `404 Not Found`**: `trackId` is not the calling Session's current track, or the provider has no lyrics for `candidateId`.
- **Response `502 Bad Gateway`**: the provider could not be reached while loading the candidate, or a provider could not be reached during a `refetch`. Nothing is changed and nothing is broadcast.

`GET /api/lyrics/{trackId}` also honors overrides: it returns the chosen candidate's lyrics, or `404` for a `noLyrics` override.

---

## Song Lookup Endpoints

Find a song from a remembered, possibly misremembered, lyric line, then play or queue it on Spotify. See [Find a Song by Lyrics](../user-guide/song-lookup.md) for the user view and [Song Lookup](../architecture/lyrics-caching.md#song-lookup-by-remembered-lyrics) for how matching works.

Both endpoints are owner-only and rate limited per client address, each counted on its own: 20 search requests and 12 plays per 60 seconds by default (see [`SongLookup`](configuration.md#song-lookup-songlookup)). The limit is counted before the credential check, so unauthenticated calls spend permits too. Over the limit they return `429 Too Many Requests` with no `Retry-After` header. IPv6 addresses in the same `/64` count as one client, as for the [pairing limits](#display-endpoints).

Enum values on the wire are strings. Spotify and provider problems never change the HTTP status: they are reported inside a `200` body, because `403` and `429` already mean "Display token" and "inbound limit".

---

### `POST /api/lookup/search`
Ranks songs that match a lyric fragment: the server's lyrics library first, then online lyrics search when the library has no clear answer.

- **Auth**: requires a Session (cookie, `Authorization: Bearer <session id>`, or `access_token` query parameter).
- **Request Body**:
```json
{ "fragment": "amazing grace how sweet the sound", "scope": "local" }
```
  - `fragment` (`string`): at most 200 characters, with at least 3 words or 4 CJK characters. Longer fragments are cut to the first 24 words or 48 CJK characters.
  - `scope` (`string`, optional): `local`, `auto` (the default when missing) or `online`.
- **Behavior**:
  1. **Library**: every song with lyrics in the server's cache or overrides is scored against the fragment. The library is server-wide, so it includes tracks any Session on this server has played.
  2. **Decision**: the best guess is `accepted` when it scores at least `0.70` and leads the next different song by at least `0.10`. Otherwise the outcome is `ambiguous` (best score at least `0.50`) or `none`.
  3. **Online**: with `scope: "auto"` and an outcome that is not `accepted`, or with `scope: "online"`, NetEase lyric search runs first (1 request). LRCLIB title search (up to 2 requests) runs only while the outcome is still not `accepted`; with NetEase disabled, `scope: "online"` always asks LRCLIB. At most 3 provider requests and 10 seconds per lookup; a timeout returns what was found so far.
  4. **Merge**: online hits are scored with the same scorer, ranked with the library guesses, and decided again. Versions of the same song (for example a remaster under another track id) are grouped into one guess.
  5. `scope: "local"` never calls a provider and reports enabled providers as `skipped`, or as `rateLimited` when the outcome is not `accepted` and the caller's online budget is spent.
  6. Each Spotify user may run 6 online lookups per minute (`SongLookup:OnlineLookupsPerMinute`). Answers reused from the 10-minute in-memory query cache do not count. When the budget is spent, providers report `rateLimited` and library guesses are still returned.
  7. A lookup never writes the lyrics cache or the negative cache.
- **Response `200 OK`**:
```json
{
  "outcome": "accepted",
  "onlineRecommended": false,
  "guesses": [
    {
      "source": "Local",
      "spotifyTrackId": "1a2b3c4d5e6f7g8h9i0jKL",
      "title": "Amazing Grace",
      "artist": "The Example Singers",
      "album": "Hymns",
      "durationMs": null,
      "confidence": 0.93,
      "verified": true,
      "matchedLines": [
        "Amazing grace, how sweet the sound",
        "That saved a wretch like me"
      ],
      "versionCount": 2
    }
  ],
  "providers": [
    { "name": "NetEase", "status": "skipped", "retryAfterSeconds": null },
    { "name": "LRCLIB", "status": "skipped", "retryAfterSeconds": null }
  ]
}
```
  - `outcome`: `accepted`, `ambiguous` or `none`. `none` can still carry low-scoring guesses.
  - `onlineRecommended`: `true` only for `scope: "local"` when the outcome is not `accepted`, a provider is enabled and the caller's online budget is not spent. Clients then repeat the search with `scope: "auto"`, which spends a second inbound permit.
  - `guesses`: at most 5 guesses scoring at least `0.40`, best first, followed by up to 3 NetEase hits the scorer could not confirm (`verified: false`, for example Traditional against Simplified Chinese).
  - `source`: `Local`, `NetEase` or `LRCLIB`. Only `Local` guesses have a `spotifyTrackId`; `durationMs` is `null` for them.
  - `confidence`: match score from `0` to `1`, rounded to 2 decimals. It is not a probability.
  - `matchedLines`: up to 2 lyric lines where the fragment matched best.
  - `versionCount`: how many versions were grouped into this guess.
  - `providers[].status`: `ok`, `skipped`, `disabled`, `unavailable` or `rateLimited`. `retryAfterSeconds` is set only for `rateLimited`.
- **Response `400 Bad Request`**: `fragment` is missing, blank, too short or over 200 characters, or `scope` is unknown. The body is `{ "message": "..." }`.
- **Response `401 Unauthorized`**: no Session credential, or the Session is unknown.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token). No lyrics are read and no provider is called.
- **Response `429 Too Many Requests`**: inbound rate limit reached.

---

### `POST /api/lookup/play`
Plays or queues a guess on the owner's active Spotify device.

- **Auth**: requires a Session (cookie, `Authorization: Bearer <session id>`, or `access_token` query parameter).
- **Request Body**: a library guess by id, or an online guess by title and artist.
```json
{ "action": "play", "spotifyTrackId": "1a2b3c4d5e6f7g8h9i0jKL" }
```
```json
{ "action": "queue", "title": "Amazing Grace", "artist": "The Example Singers", "durationMs": 245000 }
```
  - `action` (`string`): `play` or `queue`.
  - `spotifyTrackId` (`string`): a 22-character Spotify track id. Takes precedence over `title` and `artist`.
  - `title`, `artist` (`string`, at most 300 characters each), `durationMs` (`int`, optional): used when there is no track id.
- **Behavior**:
  1. While Spotify calls are paused by a recent `429`, nothing is sent and the status is `rateLimited`.
  2. A guess without a track id is searched on Spotify at click time (track search on the title and primary artist, then one plain-text retry, 5 results each). A hit is accepted only when its title matches and either an artist matches on whole words or the duration is within 3 seconds. Otherwise the status is `notOnSpotify` and nothing plays.
  3. `play` starts the track at once and replaces the current playback context (album or playlist). `queue` adds it to the end of the user's Spotify queue.
  4. After a successful `play`, the server polls playback twice shortly after, as for transport commands, so viewers see the new track and its lyrics quickly.
- **Response `200 OK`**:
```json
{
  "status": "success",
  "track": {
    "id": "1a2b3c4d5e6f7g8h9i0jKL",
    "title": "Amazing Grace",
    "artist": "The Example Singers",
    "album": "Hymns",
    "albumArtUrl": "https://i.scdn.co/image/ab67616d0000b273example",
    "durationMs": 245000,
    "isExplicit": false
  },
  "retryAfterSeconds": null
}
```
  - `status`: `success`, `notOnSpotify`, `noActiveDevice`, `missingPermissions` (no playback scope, or not a Premium account), `rateLimited` or `failed`.
  - `track`: the Spotify track a title and artist resolved to, so the UI can show what actually started. `null` for a play by id.
  - `retryAfterSeconds`: whole seconds until Spotify may be called again, set only for `rateLimited`.
- **Response `400 Bad Request`**: unknown `action`, a malformed `spotifyTrackId`, neither a track id nor both `title` and `artist`, or a `title` or `artist` over 300 characters. The body is `{ "message": "..." }`.
- **Response `401 Unauthorized`**: no Session credential, or the Session is unknown.
- **Response `403 Forbidden`**: the caller holds a Display token. Nothing is sent to Spotify.
- **Response `429 Too Many Requests`**: inbound rate limit reached.

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
