# REST API Reference

In addition to the real-time SignalR hub, Cantus provides ASP.NET Core Minimal API HTTP endpoints for authentication, lyrics, Displays and health checks.

JSON bodies use camelCase property names in both directions (request property names are matched case-insensitively). Error responses for `400`, `404` and `502` carry `{ "message": "..." }`; `401`, `403` and `429` have no body. An unhandled error returns `500` as `application/problem+json` with a `traceId`. The sign-in endpoints are the exception: they answer a browser, so most of their failures are HTML pages. [`POST /api/auth/local`](#post-apiauthlocal) is another: its `400`, `401`, `403` and `503` carry `{ "error": "<code>" }`.

In the Development environment the server also serves its OpenAPI document at `/openapi/v1.json` and a Scalar API browser under `/scalar`.

---

## Authentication Endpoints

### Session credential
Endpoints that act on the caller's Session read its credential from these slots, in order: the `cantus_session_id` cookie, the `access_token`, `token` or `session_id` query parameter, or the `Authorization` header (`Bearer <id>` or the raw id). Only the first slot that holds a value is checked as a Session id.

The credential is the Session id issued by the OAuth callback (a Spotify Session) or by [`POST /api/auth/local`](#post-apiauthlocal) (a Local Session). The server matches it against that exact id only. A Spotify user id is not a credential: `/me` and `POST /api/lyrics/offset` return `401`, `/sessions` returns an empty list, and revoke or logout leaves every Session untouched.

### Display token
A Display (a paired screen) holds a view-only Display token instead of a Session. It presents the token in the `cantus_display_token` cookie, the `display_token` query parameter, or any of the Session credential slots (the SignalR client sends it as `access_token` or `Authorization: Bearer`). When a request carries both a valid Session and a Display token, the Session wins. When the first slot does not hold a valid Session, every slot is tried as a Display token, so a stale `cantus_session_id` cookie does not hide a Display token sent in `access_token` or `Authorization`.

The server stores only a SHA-256 hash of each token. A revoked or unknown token is treated as no credential.

A Display token is rejected with `403 Forbidden` by every owner-only endpoint: `GET /api/auth/me`, `GET /api/auth/sessions`, `DELETE /api/auth/sessions/{userId}`, `POST /api/auth/logout`, `GET /api/lyrics/{trackId}/candidates`, `POST /api/lyrics/{trackId}/override`, `POST /api/lyrics/offset`, `POST /api/lookup/search`, `POST /api/lookup/play`, and every [Display endpoint](#display-endpoints) except `POST /api/displays/pairings`. Nothing is changed or revoked.

---

### `GET /api/auth/spotify/login` (or `GET /api/auth/login`)
Starts the Spotify OAuth 2.0 PKCE flow. No credential needed.

- **Query Parameters**:
    - `json` (`bool`, optional): if `true`, returns `200 OK` with `{ "authorizationUrl": "..." }` instead of a redirect.
    - `spotify_client_id` (`string`, optional): Client ID of the user's [own Spotify app](../operator-guide/spotify-setup.md#5-users-own-spotify-apps), used for this sign-in instead of the server's. It is kept in the `cantus_spotify_client_id` cookie for the callback.
    - `client_id` (`string`, optional): ephemeral client handshake id. After a successful callback the server sends [`ReceiveAuthSession`](signalr-api.md#receivesessions-receiveauthsession-and-receivesessionrevoked) to the hub group `client_{clientId}` (joined with `RegisterClientLogin`) and to the connection whose id is `clientId`, plus `ReceiveSessions` to that group.
- **Response**: `302 Found` redirect to Spotify's `https://accounts.spotify.com/authorize` with the generated `code_challenge`, `client_id`, `state` and requested scopes. The state, PKCE verifier, redirect URI and `client_id` are kept in short-lived (10 minute) HTTP-only cookies for the callback.
- **Failure pages** (HTML): `403 Forbidden` when `spotify_client_id` is sent but users' own Spotify apps are turned off, `400 Bad Request` when it is not a valid Client ID, and `503 Service Unavailable` when no Spotify Client ID is configured.

---

### `GET /api/auth/spotify/setup`
Describes how this server signs in to Spotify. The **Connect Spotify** screen uses it to guide a user through creating their own Spotify app. No credential needed.

- **Response `200 OK`**:
```json
{
  "serverClientIdConfigured": true,
  "userClientIdsAllowed": false,
  "redirectUri": "https://cantus.yourdomain.com/api/auth/spotify/callback",
  "redirectUriAccepted": true,
  "dashboardUrl": "https://developer.spotify.com/dashboard/create",
  "localSessionsEnabled": true,
  "localSessionJoinCodeRequired": true
}
```

  - `redirectUri`: the callback this server would send to Spotify for the current request.
  - `redirectUriAccepted`: `false` when Spotify will refuse that callback (plain `http` on anything but a loopback IP literal, which includes `localhost` and LAN addresses).
  - `localSessionsEnabled`: `LocalSessions__Enabled`. When `true`, the Linux desktop app offers **Use this computer's players**.
  - `localSessionJoinCodeRequired`: `true` when Local Sessions are enabled and `LocalSessions__JoinCode` is set, so the client asks for the code before calling [`POST /api/auth/local`](#post-apiauthlocal). The code itself is never sent.

---

### `GET /api/auth/spotify/callback` (or `GET /api/auth/callback`)
Handles Spotify's redirect after user consent.

- **Query Parameters**:
    - `code` (`string`): Spotify authorization code.
    - `state` (`string`): anti-forgery state, checked against the login cookie.
    - `error` (`string`, optional): set by Spotify when consent fails.
- **Behavior**:
    1. Validates the state and exchanges the code (with the PKCE verifier) for access and refresh tokens.
    2. Encrypts the tokens with ASP.NET Core Data Protection and saves the Session in SQLite.
    3. Registers the Session in the in-memory playback registry, keeping any playback already tracked for it.
    4. Sends `ReceiveSessions` to the Session's own hub group `user_{sessionId}`, and the `client_id` handoff events described above.
    5. Sets the `cantus_session_id` cookie (HTTP-only, 30 days).
- **Response `200 OK`**: HTML page that opens `cantus://auth?session_id=<id>` for the desktop app and links to the web player.
- **Failure pages** (HTML, see [sign-in failure pages](../operator-guide/troubleshooting.md#sign-in-failure-pages)):
    - `200 OK`: the user cancelled at Spotify (`error=access_denied`).
    - `400 Bad Request`: any other Spotify `error`, a state mismatch, a missing PKCE verifier cookie, an invalid own-app Client ID, or a code exchange that failed for a known reason.
    - `403 Forbidden`: the sign-in used the user's own Spotify app, and that is turned off.
    - `502 Bad Gateway`: the code exchange failed for an unknown reason.
- **Response `400 Bad Request`** (JSON string): `code` is missing.

---

### `POST /api/auth/local`
Creates a [Local Session](../user-guide/local-players.md): a Session with no Spotify account, whose playback the desktop app publishes with [`PublishLocalPlayback`](signalr-api.md#publishlocalplaybackupdate). No credential needed. Off unless `LocalSessions__Enabled` is `true` (see [`LocalSessions`](configuration.md#local-sessions-localsessions)).

Rate limited per client address: 5 requests per 600 seconds by default, counted before any other check. IPv6 addresses in the same `/64` count as one client.

- **Request Body**:
```json
{ "displayName": "jaret-desktop", "joinCode": "choose-a-long-code" }
```
  - `displayName` (`string`, required): the Session's name, usually the computer's name. Trimmed, then 1 to 64 characters.
  - `joinCode` (`string`, optional): required when the server sets `LocalSessions__JoinCode`. Compared in constant time.
- **Behavior**: first deletes Local Sessions idle for longer than `LocalSessions__IdleExpiryDays` (with their Displays, as on [logout](#post-apiauthlogout)), then creates the Session and sets the `cantus_session_id` cookie (HTTP-only, 30 days), like the OAuth callback.
- **Response `200 OK`**: one Session object, shaped like an item of [`GET /api/auth/sessions`](#get-apiauthsessions), with `kind: "Local"` and `spotifyUserId`, `email` and `profileImageUrl` set to `null`. Its `id` is the Session credential.
- **Errors** (`{ "error": "<code>" }`, checked in this order):

| Status | `error` | Meaning |
| :--- | :--- | :--- |
| `403 Forbidden` | `local_sessions_disabled` | `LocalSessions__Enabled` is off. |
| `401 Unauthorized` | `join_code_required` | The server needs a join code and none was sent. |
| `401 Unauthorized` | `join_code_invalid` | The join code is wrong. |
| `400 Bad Request` | `invalid_display_name` | `displayName` is missing, blank or longer than 64 characters. |
| `503 Service Unavailable` | `local_session_limit` | `LocalSessions__MaxSessions` Local Sessions already exist. |

- **Response `429 Too Many Requests`**: over the rate limit. No body.

---

### `GET /api/auth/sessions`
Returns the caller's own Session as a one-item list.

- **Response `200 OK`**:
```json
[
  {
    "id": "<Session id>",
    "kind": "Spotify",
    "spotifyUserId": "spotify_user_12345",
    "displayName": "Jane Doe",
    "email": "jane@example.com",
    "profileImageUrl": "https://i.scdn.co/image/...",
    "isCurrentlyPlaying": true,
    "createdAtUtc": "2026-08-26T04:30:00+00:00",
    "updatedAtUtc": "2026-08-26T04:30:00+00:00"
  }
]
```
  An empty list when the caller has no valid credential. `kind` is `Spotify` or `Local`; a Local Session has `spotifyUserId`, `email` and `profileImageUrl` set to `null`.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token).

---

### `GET /api/auth/me`
Returns the caller's Session.

- **Response `200 OK`**: one Session object, shaped like an item of `GET /api/auth/sessions` (`isCurrentlyPlaying` is always `false` here).
- **Response `401 Unauthorized`**: no valid credential.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token).

---

### `DELETE /api/auth/sessions/{userId}`
Revokes a Session and removes its encrypted tokens from SQLite. `{userId}` must be the caller's own Session id, matching the [session credential](#session-credential).

The owner's Displays are deleted with the Session. Each of their live hub connections receives [`ReceiveDisplayRevoked`](signalr-api.md#receivedisplayrevoked) and is then closed, the same as [revoking a Display](#delete-apidisplaysdisplayid). Other owners' Displays are untouched. The Session's hub group then receives `ReceiveSessionRevoked` and an empty `ReceiveSessions`.

- **Response `200 OK`**: `{ "message": "Session '<userId>' revoked." }`
- **Response `401 Unauthorized`**: no credential.
- **Response `403 Forbidden`**: the caller holds a Display token, or `{userId}` is not the caller's credential.
- **Response `404 Not Found`**: no Session has that id.

---

### `POST /api/auth/logout`
Revokes the caller's Session (if any) and deletes the `cantus_session_id` cookie. For a Local Session it also drops the playback state the server holds for it. Like [`DELETE /api/auth/sessions/{userId}`](#delete-apiauthsessionsuserid), it deletes the owner's Displays, closes their live hub connections after sending each one [`ReceiveDisplayRevoked`](signalr-api.md#receivedisplayrevoked), and sends `ReceiveSessionRevoked` and an empty `ReceiveSessions` to the Session's hub group.

- **Response `200 OK`**: `{ "message": "Logged out and session revoked." }`, also when the caller had no credential.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token).

---

## Lyrics & Playback Endpoints

### `GET /api/lyrics/{trackId}`
Returns the stored lyrics for a Spotify track id. No credential needed.

- **Response `200 OK`**:
```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "title": "Never Gonna Give You Up",
  "artist": "Rick Astley",
  "album": "Whenever You Need Somebody",
  "isSynced": true,
  "isInstrumental": false,
  "lines": [
    {
      "timestampMs": 18450,
      "text": "We're no strangers",
      "syllables": [
        { "timestampMs": 18450, "durationMs": 420, "text": "We're " },
        { "timestampMs": 18870, "durationMs": 310, "text": "no " },
        { "timestampMs": 19180, "durationMs": 900, "text": "strangers" }
      ]
    },
    {
      "timestampMs": 22800,
      "text": "You know the rules and so do I",
      "syllables": null
    }
  ],
  "plainLyrics": null
}
```

- **Response `404 Not Found`**: nothing is cached for the track, or it has a `noLyrics` override.

`syllables` carries real per-word timing (absolute milliseconds; a word's trailing space stays in its `text`) and is `null` for lines with line-level timing only.

---

### `POST /api/lyrics/offset`
Saves a manual timing offset for a track. The hub method [`SetTrackOffset`](signalr-api.md#settrackoffsettrackid-offsetms) does the same.

- **Request Body**:
```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "offsetMs": 150
}
```

- Requires the caller's [session credential](#session-credential).
- **Behavior**: stores the offset (server-wide, per track) and sends `ReceiveTrackOffset` to the caller's Session connections and Displays.
- **Response `200 OK`**: the saved request body.
- **Response `400 Bad Request`**: `trackId` is missing or blank.
- **Response `401 Unauthorized`**: no credential, or the credential does not match a Session id.
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token).

---

### `GET /api/lyrics/{trackId}/candidates`
Lists the lyrics every enabled provider has for the owner's current track, so a wrong automatic match can be replaced.

- **Auth**: requires the caller's [session credential](#session-credential).
- **Path Parameters**:
    - `trackId` (`string`): Spotify track ID. Must be the track currently playing for the calling Session, since providers search by its title, artist and duration.
- **Behavior**:
    1. Queries LRCLIB and, when `Netease:Enabled` is `true`, NetEase in parallel. Nothing is cached or changed.
    2. LRCLIB returns every search result that has synced lyrics, plain lyrics, or an instrumental flag.
    3. NetEase returns synced lyrics or an instrumental marker (`isSynced: false`, `isInstrumental: true`, for songs NetEase flags `nolyric` or `pureMusic`), from the first 5 search results by a matching artist. Duration does not filter candidates.
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
  "unavailableProviders": ["NetEase"],
  "activeOverride": {
    "kind": "candidate",
    "provider": "LRCLIB",
    "candidateId": "3396226"
  }
}
```

- `preview` holds the first non-blank lyric lines, 2 at most. It is empty for an instrumental candidate.
- `activeOverride` is the [override](#post-apilyricstrackidoverride) in effect for the track, or `null` when it uses automatic matching. `kind` is `candidate` or `noLyrics`; `provider` and `candidateId` are `null` for `noLyrics`.
- **Response `401 Unauthorized`**: no Session credential, or the Session is unknown.
- **Response `403 Forbidden`**: the caller holds a Display token. No provider is queried.
- **Response `404 Not Found`**: `trackId` is not the calling Session's current track, or nothing is playing.

---

### `POST /api/lyrics/{trackId}/override`
Corrects the lyrics for the owner's current track. An override is stored per track and applies server-wide, like track offsets. It survives restarts, cache expiry and negative caching until it is cleared.

- **Auth**: requires the caller's [session credential](#session-credential).
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

- **Actions** (`action` is required and case-sensitive; `provider` is matched ignoring case):

| `action` | Effect |
| :--- | :--- |
| `candidate` | Loads the candidate's lyrics from `provider` (as listed by `GET /api/lyrics/{trackId}/candidates`) and stores them as the override. `provider` and `candidateId` are required. |
| `noLyrics` | Stores a "no lyrics" override. Viewers see an empty lyrics view and no provider is queried for the track. |
| `refetch` | Queries the providers again, skipping the caches and preferring a word-timed result. A definitive answer replaces the cached automatic match (including a "not found" entry) and removes any override. If a provider cannot be reached, nothing is changed. |
| `clear` | Removes any override and returns the track to automatic matching, served from the cache when it still holds a match. A line-only cached match can then get a word timing upgrade attempt (see [Lyrics Caching](../architecture/lyrics-caching.md#upgrading-line-only-entries)). |

- **Behavior**: after a successful action, every Session currently on the track (not only the caller) and its Displays receive the new lyrics through the hub's `ReceiveLyrics`. A `noLyrics` result, or a re-fetch that finds nothing, is sent as an empty lyrics payload.
- **Response `200 OK`**: the lyrics now in effect, in the same shape as `GET /api/lyrics/{trackId}`. With no lyrics, `lines` is empty, `isSynced` is `false`, and `title`, `artist` and `album` come from the current track.
- **Response `400 Bad Request`**: missing or unknown `action`, `candidate` without `provider` or `candidateId`, or `provider` is not an enabled provider.
- **Response `401 Unauthorized`**: no Session credential, or the Session is unknown.
- **Response `403 Forbidden`**: the caller holds a Display token. Nothing is changed and nothing is broadcast.
- **Response `404 Not Found`**: `trackId` is not the calling Session's current track (or nothing is playing), or the provider has no lyrics for `candidateId`.
- **Response `502 Bad Gateway`**: the provider could not be reached while loading the candidate, or a provider could not be reached during a `refetch`. Nothing is changed and nothing is broadcast.

`GET /api/lyrics/{trackId}` also honors overrides: it returns the chosen candidate's lyrics, or `404` for a `noLyrics` override.

---

## Display Endpoints

A Display is paired in three steps:

1. The screen opens an anonymous [PlaybackHub](signalr-api.md) connection and calls `POST /api/displays/pairings` with its connection id. It shows the returned code.
2. The owner enters the code on a signed-in device, which calls `POST /api/displays/pairings/approve` with a name for the Display.
3. The server sends the new Display token to the screen's connection as [`ReceiveDisplayPaired`](signalr-api.md#receivedisplaypaired). The screen stores the token and reconnects with it.

The Cantus client's pairing screen shows the code and a QR of the approval link `<server base URL>/?pair=<CODE>`, the app's own address with the code in the `pair` query parameter. Opening the link in the web app shows the owner's approval prompt with the code filled in; it asks for the Display's name and calls `POST /api/displays/pairings/approve`. The client's **Displays** card lists, renames and revokes Displays with the endpoints below.

After an approve, rename or revoke, and when a Display connects, the server sends [`ReceiveDisplaysChanged`](signalr-api.md#receivedisplayschanged) to the owner's Session connections, so a client that shows the list fetches `GET /api/displays` again. Changes made on another device show at once.

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
  "expiresAtUtc": "2026-11-20T18:05:00+00:00"
}
```

- **Response `400 Bad Request`**: `connectionId` is missing or is not a live PlaybackHub connection. Call this only after the connection has started.
- **Response `429 Too Many Requests`**: rate limit reached.
- **Response `503 Service Unavailable`**: too many pairings are waiting for approval (`DisplayPairing:MaxPendingPairings`). The body is a ProblemDetails object.

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
  "createdAtUtc": "2026-11-20T18:01:12+00:00",
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

## Song Lookup Endpoints

Find a song from a remembered, possibly misremembered, lyric line, then play or queue it on Spotify. See [Find a Song by Lyrics](../user-guide/song-lookup.md) for the user view and [Song Lookup](../architecture/lyrics-caching.md#song-lookup-by-remembered-lyrics) for how matching works.

Both endpoints need a Spotify Session: a Display token or a [Local Session](#post-apiauthlocal) gets `403 Forbidden`. They are rate limited per client address, each counted on its own: 20 search requests and 12 plays per 60 seconds by default (see [`SongLookup`](configuration.md#song-lookup-songlookup)). The limit is counted before the credential check, so unauthenticated calls spend permits too. Over the limit they return `429 Too Many Requests` with no `Retry-After` header. IPv6 addresses in the same `/64` count as one client, as for the [pairing limits](#display-endpoints).

Enum values on the wire are strings. Spotify and provider problems never change the HTTP status: they are reported inside a `200` body, because `403` and `429` already mean "Display token" and "inbound limit".

---

### `POST /api/lookup/search`
Ranks songs that match a lyric fragment: the server's lyrics library first, then online lyrics search when the library has no clear answer.

- **Auth**: requires a Session (cookie, `Authorization: Bearer <session id>`, or `access_token` query parameter).
- **Request Body**:
```json
{ "fragment": "amazing grace how sweet the sound", "scope": "local" }
```

  - `fragment` (`string`): at most 200 characters, with at least 3 words or 4 CJK characters. Longer fragments are cut to the first 24 words or 48 CJK characters. Mixed text shares one budget in which a word counts as two CJK characters: the minimum is 6 units and the cut is at 48.
  - `scope` (`string`, optional): `local`, `auto` (the default when missing) or `online`.
- **Behavior**:
    1. **Library**: every song with lyrics in the server's cache or overrides is scored against the fragment. The library is server-wide, so it includes tracks any Session on this server has played.
    2. **Decision**: the best guess is `accepted` when it scores at least `0.70` and leads the next different song by at least `0.10`. Otherwise the outcome is `ambiguous` (best score at least `0.50`) or `none`.
    3. **Online**: with `scope: "auto"` and an outcome that is not `accepted`, or with `scope: "online"`, NetEase lyric search runs first (1 request). LRCLIB title search (up to 2 requests) runs only while the outcome is still not `accepted`; with NetEase disabled, `scope: "online"` always asks LRCLIB. At most 3 provider requests and 10 seconds per lookup; a timeout returns what was found so far.
    4. **Merge**: online hits are scored with the same scorer, ranked with the library guesses, and decided again. Versions of the same song (for example a remaster under another track id) are grouped into one guess.
    5. `scope: "local"` never calls a provider and reports enabled providers as `skipped`, or as `rateLimited` when the outcome is not `accepted` and the caller's online budget is spent.
    6. Each Spotify user may run 6 online lookups per minute (`SongLookup:OnlineLookupsPerMinute`). Answers reused from the in-memory query cache (kept 10 minutes, or 2 minutes when nothing was found) do not count. When the budget is spent, providers report `rateLimited` and library guesses are still returned.
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
- **Response `403 Forbidden`**: the caller holds a [Display token](#display-token) or a Local Session. No lyrics are read and no provider is called.
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
    4. After a successful `play`, the server polls playback at once and twice more shortly after, as for transport commands, so viewers see the new track and its lyrics quickly.
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
- **Response `403 Forbidden`**: the caller holds a Display token or a Local Session. Nothing is sent to Spotify.
- **Response `429 Too Many Requests`**: inbound rate limit reached.

---

## Health & Monitoring Endpoints

### `GET /api/health`
Liveness and readiness probe for Docker or Kubernetes. No credential needed.

- **Response `200 OK`**:
```json
{
  "status": "Healthy",
  "version": "1.0.0",
  "activeSessions": 1,
  "database": "Connected",
  "commitSha": "7130010"
}
```
  `activeSessions` counts Sessions with playback tracked in memory. `version` and `commitSha` come from the build.
- **Response `503 Service Unavailable`**: the database cannot be reached. Same shape, with `"status": "Degraded"` and `"database": "Disconnected"`.
