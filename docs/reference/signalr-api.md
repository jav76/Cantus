# SignalR PlaybackHub Protocol Reference

The Cantus real-time protocol runs over **ASP.NET Core SignalR** (JSON protocol, camelCase payloads). It pushes playback state, lyrics and offsets, takes player commands and published local playback, and answers clock sync samples.

---

## Hub Endpoint

```http
WSS /hubs/playback
```

Clients negotiate connection parameters over HTTP and upgrade to WebSockets. The server sends a keep-alive every 15 seconds and drops a client it has not heard from in 30 seconds.

On connect, the hub reads the connection's credential from the same sources as the REST API (see [Session credential](rest-api.md#session-credential) and [Display token](rest-api.md#display-token)). A Session matches only its exact Session id. A connection with no valid credential stays anonymous and joins no owner group.

---

## Credential Kinds

A connection holds a Session or a [Display token](rest-api.md#display-token). A Display passes its token the same way as on REST: the `cantus_display_token` cookie, the `display_token` query parameter, or the client's access token factory (sent as `access_token` on WebSockets and `Authorization: Bearer` on negotiate). The credential is resolved once, when the connection opens. `SubscribeToUser` is the exception: it resolves the credential again on each call, so it does nothing once the Session is revoked.

| | Session | Display token | Anonymous |
| :--- | :--- | :--- | :--- |
| Group joined on connect | `user_{sessionId}` | `display_{ownerSessionId}` | None |
| Initial snapshot (playback, lyrics, offset) | Yes | Yes | No |
| Initial `ReceiveDiagnostics` | Yes | Yes, `activeUserId` is `null` | Yes, idle with no user |
| Initial `ReceiveSessions` | Own Session | Never | Empty list |
| `ReceiveAuthSession` / `ReceiveSessionRevoked` / `ReceiveDisplaysChanged` | Yes | Never | `ReceiveAuthSession` and `ReceiveSessions`, for a sign-in started with this connection's `client_id` |
| `SyncClock` | Yes | Yes | Yes |
| `ReportClientVisibility`, `RefreshPlayback` | Yes | Yes, forced polls throttled to one per owner every 5 seconds | No effect (no owner) |
| `SubscribeToUser` | Yes | No-op (bound to its owner) | No-op |
| `SendPlayerCommand`, `SetPlayerVolume`, `SeekPlayback`, `SetShuffle`, `SetRepeat` | Yes (a Local Session gets `3`, `Failed`) | Rejected: returns `3` (`Failed`) | Returns `3` (`Failed`) |
| `PublishLocalPlayback` | Local Session only (a Spotify Session gets `1`) | Returns `1` (`NotLocalSession`) | Returns `1` (`NotLocalSession`) |
| `SetTrackOffset` | Yes | Rejected: ignored | Ignored |
| `RegisterClientLogin` | Yes | Rejected: ignored | Yes |

Every playback broadcast goes to both groups. Displays get the same payloads with `activeUserId` set to `null`, because the owner's Session id is itself a credential. A Display connection counts as a viewer of its owner for [adaptive polling](../architecture/adaptive-polling.md).

Because the credential is resolved only at connect, the server tracks each Display's open connections. [Revoking a Display](rest-api.md#delete-apidisplaysdisplayid), or ending its owner's Session through [logout](rest-api.md#post-apiauthlogout) or [Session revoke](rest-api.md#delete-apiauthsessionsuserid), sends `ReceiveDisplayRevoked` to them and then closes them. A connection whose token was validated just before a revocation (up to 60 seconds) is closed as soon as it finishes connecting.

---

## Pairing a Display

An anonymous connection becomes a Display through the [pairing endpoints](rest-api.md#display-endpoints):

1. Connect to the hub with no credential and wait for the connection to start.
2. Send the connection id to `POST /api/displays/pairings` and show the returned code (and, optionally, a QR of it).
3. Once an owner approves the code, the server sends `ReceiveDisplayPaired` to this connection.
4. Store the token, then reconnect with it. The current connection stays anonymous.

Disconnecting before approval cancels the pending code, and disconnecting during approval removes the Display it would have created. Request a new code after reconnecting.

---

## Client-to-Server Methods (Invocations)

Clients invoke these methods on the hub:

### `SubscribeToUser(userId)`
Re-subscribes a Session connection to its own playback. A Session connection is already subscribed when it connects, so this is only needed to refresh the snapshot or to unsubscribe.

- **Parameters**: `userId` (`string?`): the caller's Session id, or `null` to unsubscribe. Ignored unless the connection's credential resolves to that same Session id.
- **Behavior**: joins `user_{userId}` and records the subscription in the playback registry, then sends the current `ReceivePlaybackState`, `ReceiveLyrics` and `ReceiveTrackOffset` (each only when there is something to send) and a `ReceiveDiagnostics`. A no-op on a Display or anonymous connection.

### `RegisterClientLogin(clientId)`
Joins the sign-in handoff group `client_{clientId}`.

- **Parameters**: `clientId` (`string`): temporary client handshake id, the same value passed as `client_id` to [`GET /api/auth/login`](rest-api.md#get-apiauthspotifylogin-or-get-apiauthlogin).
- **Behavior**: lets a client that is not signed in yet receive `ReceiveAuthSession` (and `ReceiveSessions`) when the browser OAuth flow completes. Ignored on a Display connection, since the handoff grants owner rights.

### `SyncClock(clientSendTimeMs)`
Takes one clock sync sample.

- **Parameters**: `clientSendTimeMs` (`long`): the client's Unix time in milliseconds when it sent the call ($t_0$).
- **Returns**: a `ClockSyncResponse` with `clientSendTimeMs` ($t_0$), `serverReceiveTimeMs` ($t_1$) and `serverSendTimeMs` ($t_2$), all Unix milliseconds. The client records its own receive time ($t_3$) to compute offset and round trip.

```json
{
  "clientSendTimeMs": 1788225300000,
  "serverReceiveTimeMs": 1788225300015,
  "serverSendTimeMs": 1788225300015
}
```

### `SetTrackOffset(trackId, offsetMs)`
Stores a manual timing offset for a track, like [`POST /api/lyrics/offset`](rest-api.md#post-apilyricsoffset).

- **Parameters**:
    - `trackId` (`string`): Spotify track id.
    - `offsetMs` (`int`): offset in milliseconds (e.g. `150` or `-50`).
- **Behavior**: persists the offset in SQLite and sends `ReceiveTrackOffset` to the owner's Session connections and Displays. Requires a subscribed Session; ignored on a Display connection, for a blank `trackId`, or without a subscription. Returns nothing.

### `ReportClientVisibility(isVisible)`
Tells the server whether the client window or browser tab is visible.

- **Parameters**: `isVisible` (`bool`): `true` if visible; `false` if minimized, hidden or tabbed away.
- **Behavior**: updates the connection's visibility in the playback registry. If `isVisible` is `true`, it also requests an immediate poll, like `RefreshPlayback`. On a Display connection the poll request is throttled like `RefreshPlayback`.

### `RefreshPlayback()`
Requests an immediate Spotify playback poll for the connection's owner.

- **Behavior**: calls `IPlaybackSessionRegistry.RequestUserActivity`, which cuts the background poller's wait short.
- **Display connections**: calls `IPlaybackSessionRegistry.RequestDisplayActivity` instead, which forwards at most one request per owner every 5 seconds. Extra calls inside that window are dropped; the regular adaptive poll still runs.

### Player commands
Control the owner's Spotify playback. Each returns an `int` (`PlayerCommandResult`):

| Value | Name | Meaning |
| :--- | :--- | :--- |
| `0` | `Success` | Spotify accepted the command. The server polls again right away and twice more shortly after. |
| `1` | `MissingPermissions` | Spotify returned `403`: the Session lacks the `user-modify-playback-state` scope (re-link to fix) or the account is not Premium. |
| `2` | `NoActiveDevice` | Spotify returned `404`: no active device. |
| `3` | `Failed` | Any other failure, including an unknown command, a Display or anonymous caller, a Local Session, or no stored Session. |

| Method | Parameter |
| :--- | :--- |
| `SendPlayerCommand(command)` | `command` (`string`): `pause`, `resume`, `next` or `previous`. |
| `SetPlayerVolume(volumePercent)` | `volumePercent` (`int`): clamped to 0 to 100. |
| `SeekPlayback(positionMs)` | `positionMs` (`long`): position in the current track. |
| `SetShuffle(enabled)` | `enabled` (`bool`). |
| `SetRepeat(repeatMode)` | `repeatMode` (`string`): `off`, `track` or `context`. |

### `PublishLocalPlayback(update)`
Sends what a [Local Session](rest-api.md#post-apiauthlocal)'s followed player is doing. The desktop app sends a full snapshot after every (re)connect, then on each change of player, track, play status, capabilities or a seek, and when its projected position drifts more than 400 ms from what it last sent.

- **Parameters**: `update` (`LocalPlaybackUpdateDto`):
```json
{
  "sequence": 42,
  "player": { "id": "spotify", "name": "Spotify" },
  "deviceName": "Spotify",
  "track": {
    "title": "Glimpse of Us",
    "artist": "Joji",
    "album": "SMITHEREENS",
    "durationMs": 233000,
    "albumArtUrl": "https://i.scdn.co/image/...",
    "spotifyTrackId": "2mlNgAeIBnL78ZriXgrRHz"
  },
  "isPlaying": true,
  "positionMs": 98774,
  "rate": 1.0,
  "positionAtServerMs": 1795400000000,
  "capabilities": { "canPlay": true, "canPause": true, "canGoNext": true, "canGoPrevious": true, "canSeek": true }
}
```

| Field | Rule |
| :--- | :--- |
| `sequence` | `long`, strictly increasing per connection, starting at 1. An update at or below the last one is answered `0` and ignored. |
| `player.id` | Required, at most 128 characters. `player.name` and `deviceName`: at most 128 characters. |
| `track` | `null` means nothing worth showing: the server clears the snapshot and broadcasts an empty state. When set, `title` and `artist` are required; `title`, `artist` and `album` are at most 300 characters; `durationMs` is 30000 to 1200000. |
| `track.albumArtUrl` | Kept only for `http`/`https` URLs of at most 2048 characters, otherwise dropped. |
| `track.spotifyTrackId` | Kept only when it is 22 letters and digits, otherwise dropped. It does not change the [track key](#receiveplaybackstate). |
| `positionMs` | Clamped to 0 to `durationMs`. |
| `rate` | Clamped to 0.25 to 4.0. Stored only; viewers project at 1x. |
| `positionAtServerMs` | Unix ms on the server's clock when `positionMs` was read (from [`SyncClock`](#syncclockclientsendtimems)), or `null` before the first sample. Used as the state's `timestampUtc` when within 15 seconds before to 500 ms after the server's time; otherwise the receipt time is used. |

Text fields have control characters replaced by spaces and are trimmed before these checks.

- **Returns**: an `int` (`LocalPublishResult`):

| Value | Name | Meaning |
| :--- | :--- | :--- |
| `0` | `Accepted` | Queued. The server applies it in the background and broadcasts the result. |
| `1` | `NotLocalSession` | The caller is not a Local Session (a Display, anonymous, or a Spotify Session), the Session ended, or `LocalSessions__Enabled` is off. |
| `2` | `NotPublisher` | A newer connection of the same Session publishes now. Retry with backoff; the claim frees when that connection closes. |
| `3` | `RateLimited` | The connection's budget (bursts of 10, refilled at 2 per second) is spent. Back off. |
| `4` | `Invalid` | The update broke a rule above. |

- **Behavior**: the newest connection of a Session to publish becomes its publisher. Each accepted update is sent to the owner and their Displays as a normal `ReceivePlaybackState` with `source: "Local"`, plus `ReceiveLyrics` and `ReceiveTrackOffset` when the track changes (playing or paused). A Session may start at most 20 lyric lookups per minute; a track over that budget gets empty lyrics first and its real lyrics once the budget frees. A track published without `albumArtUrl` may get one from the cover art catalogue: at once when it is cached, otherwise in a later `ReceivePlaybackState` for the same track with unchanged position and timestamp. When the publisher disconnects and none takes over within `LocalSessions__PublisherGraceSeconds` (20 by default), viewers get the track paused at its projected position, with `pollerStatus` `"Publisher offline"`.

---

## Server-to-Client Events (Broadcasts)

The server sends these events (`IPlaybackClient`):

### `ReceivePlaybackState`
Sent after each playback poll while something is playing or paused, and in the connect snapshot. When Spotify reports nothing playing, the server sends only `ReceiveDiagnostics` (`"Idle"`), not a new state.

```json
{
  "currentTrack": {
    "id": "4cOdK2wGLETKBW3PvgPWqT",
    "title": "Never Gonna Give You Up",
    "artist": "Rick Astley",
    "album": "Whenever You Need Somebody",
    "albumArtUrl": "https://i.scdn.co/image/ab67616d0000b273...",
    "durationMs": 213573,
    "isExplicit": false
  },
  "progressMs": 45200,
  "isPlaying": true,
  "timestampUtc": "2026-09-01T01:15:00+00:00",
  "deviceName": "Living room TV",
  "volumePercent": 65,
  "isShuffled": false,
  "repeatMode": "off",
  "activeUserId": "<Session id>",
  "activeUserDisplayName": "Rick",
  "source": "Spotify",
  "capabilities": null
}
```

`currentTrack`, `deviceName`, `volumePercent` and `activeUserDisplayName` can be `null`, as can `album` and `albumArtUrl` inside `currentTrack`. `activeUserId` is the owner's Session id, and `null` on Display connections.

`source` is `Spotify` (polled from the Spotify Web API) or `Local` (sent by a Local Session's [`PublishLocalPlayback`](#publishlocalplaybackupdate)). For `Local`, `capabilities` holds the player's `canPlay`, `canPause`, `canGoNext`, `canGoPrevious` and `canSeek`; it is `null` for `Spotify`. Local playback is sent while playing, paused, and as an empty state (`currentTrack` `null`) when the player stops.

The track key in `currentTrack.id` is the Spotify track id for Spotify playback. For Local playback it is `local:` followed by 32 hex characters, derived from the artist, title and duration. Lyrics, overrides and offsets are stored under it; clients should use it as given and not parse it.

### `ReceiveLyrics`
Sent when a new track starts, when a [lyrics override](rest-api.md#post-apilyricstrackidoverride) changes the lyrics, and in the connect snapshot. A track with no lyrics is sent as an empty payload (`lines` empty) so clients clear the previous track.

The payload has the same shape as [`GET /api/lyrics/{trackId}`](rest-api.md#get-apilyricstrackid): `trackId`, `title`, `artist`, `album`, `isSynced`, `isInstrumental`, `lines` and `plainLyrics`. `syllables` on a line is `null` unless it has real word timing.

### `ReceiveTrackOffset`
Sent when a timing offset is saved (hub or REST), when the track changes, and in the connect snapshot for the current track.

```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "offsetMs": 150
}
```

### `ReceiveDiagnostics`
Poller telemetry. Sent on connect, after `SubscribeToUser`, after each playback poll, and while the poller is rate limited.

```json
{
  "connectedClients": 2,
  "authorizedSessions": 1,
  "pollerStatus": "Active (Playing)",
  "activePollIntervalMs": 4000,
  "activeUserId": "<Session id>",
  "activeUserName": "Rick",
  "serverTimeUtc": "2026-09-01T01:15:00+00:00"
}
```

`activeUserId` is the owner's Session id, and `null` on Display connections. `activePollIntervalMs` is the poller's next interval; it is `0` in the diagnostics sent on connect and after `SubscribeToUser`. Those two also report only `"Active (Playing)"` or `"Idle"` as `pollerStatus`, until the next poll sends the full status.

**`pollerStatus` values** (cadences are defaults; see [adaptive polling](../architecture/adaptive-polling.md)):

- `"Active (Playing)"`: playing and at least one client is visible.
- `"Active (Background)"`: playing but every client is hidden.
- `"Paused"`: playback is paused (5s, backing off to 30s).
- `"Idle"`: nothing is playing on Spotify (10s, backing off to 60s).
- `"Rate Limited (mm:ss)"`: cooldown after an HTTP 429 from the Spotify API, with the time left (`hh:mm:ss` from one hour).

For a Local Session:

- `"Local (Playing)"` and `"Local (Paused)"`: the publisher's followed player is playing or paused.
- `"Local (Idle)"`: nothing worth showing is playing.
- `"Publisher offline"`: the desktop app disconnected and the grace period ended.

### `ReceiveSessions`, `ReceiveAuthSession` and `ReceiveSessionRevoked`
Never sent to a Display connection.

- `ReceiveSessions`: a list of Session objects, shaped like [`GET /api/auth/sessions`](rest-api.md#get-apiauthsessions), with `kind` set to `Spotify` or `Local`. Sent on connect (the caller's own Session, or an empty list when anonymous), after the OAuth callback, and as an empty list after a revoke or logout.
- `ReceiveAuthSession`: one Session object, sent after the OAuth callback to the `client_{clientId}` handoff group and to the connection whose id is `clientId`.
- `ReceiveSessionRevoked`: the revoked Session id (`string`), sent to `user_{sessionId}` on [revoke](rest-api.md#delete-apiauthsessionsuserid) or [logout](rest-api.md#post-apiauthlogout).

### `ReceiveDisplayPaired`
Sent only to the connection that requested a pairing, once an owner approves its code. `token` is the only copy of the Display token: the server keeps just its hash. `pairingId` matches the id returned by `POST /api/displays/pairings`.

```json
{
  "pairingId": "3f1c9a0e5b7d4e2a9c6b8d0f1e2a3b4c",
  "displayId": "9b2e4c6a8d0f4b1e93a5c7e9f1b3d5a7",
  "name": "Living room TV",
  "token": "<Display token>"
}
```

### `ReceiveDisplayRevoked`
Sent to each open connection of a Display right before the server closes it, because the owner revoked it or ended their Session. The payload is the Display id (`string`). The token no longer works, so the client should discard it and offer to pair again instead of reconnecting with it.

### `ReceiveDisplaysChanged`
Sent to the owner's Session connections (`user_{sessionId}`) when their Display list changes: a code is approved, a Display is renamed or revoked, or a Display connects (which can move its last-seen time). It has no payload; fetch [`GET /api/displays`](rest-api.md#get-apidisplays) again. Never sent to a Display connection or to another owner. A Display's REST calls can also move its last-seen time without this event.

### `ReceiveClockSync`
Declared on `IPlaybackClient` with the `ClockSyncResponse` shape, but the server never sends it and the Cantus client does not listen for it. Use the return value of `SyncClock`.
