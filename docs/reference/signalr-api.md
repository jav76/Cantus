# SignalR PlaybackHub Protocol Reference

The Cantus real-time streaming protocol is implemented over **ASP.NET Core SignalR**. It powers instant state synchronization, low-latency lyric broadcasting, and 4-timestamp NTP clock sync.

---

## Hub Endpoint

```http
WSS /hubs/playback
```

Clients negotiate connection parameters over HTTP and upgrade to WebSockets.

On connect, the hub reads the connection's Session credential from the same sources as the REST API and matches it against the exact Session id only. A connection whose credential does not match a Session id stays unauthenticated and joins no user group. See [Session credential](rest-api.md#session-credential).

---

## Credential Kinds

A connection holds a Session or a [Display token](rest-api.md#display-token). A Display passes its token the same way as on REST: the `cantus_display_token` cookie, the `display_token` query parameter, or the client's access token factory (sent as `access_token` on WebSockets). The credential is resolved once, when the connection opens.

| | Session | Display token |
| :--- | :--- | :--- |
| Group joined on connect | `user_{sessionId}` | `display_{ownerSessionId}` |
| Initial snapshot (playback, lyrics, offset, diagnostics) | Yes | Yes |
| `ReceiveSessions` / `ReceiveAuthSession` / `ReceiveSessionRevoked` | Yes | Never |
| `SyncClock` | Yes | Yes |
| `ReportClientVisibility`, `RefreshPlayback` | Yes | Yes, forced polls throttled to one per owner every 5 seconds |
| `SubscribeToUser` | Yes | No-op (bound to its owner) |
| `SendPlayerCommand`, `SetPlayerVolume`, `SeekPlayback`, `SetShuffle`, `SetRepeat` | Yes | Rejected: returns `3` (`Failed`) |
| `SetTrackOffset`, `RegisterClientLogin` | Yes | Rejected: ignored |

Every playback broadcast goes to both groups. Displays get the same payloads with `activeUserId` set to `null`, because the owner's Session id is itself a credential. A Display connection counts as a viewer of its owner for [adaptive polling](../architecture/adaptive-polling.md).

Because the credential is resolved only at connect, the server tracks each Display's open connections. [Revoking a Display](rest-api.md#delete-apidisplaysdisplayid) sends `ReceiveDisplayRevoked` to them and then closes them. A connection whose token was validated just before a revocation is closed as soon as it finishes connecting.

---

## Pairing a Display

An unauthenticated connection becomes a Display through the [pairing endpoints](rest-api.md#display-endpoints):

1. Connect to the hub with no credential and wait for the connection to start.
2. Send the connection id to `POST /api/displays/pairings` and show the returned code (and, optionally, a QR of it).
3. Once an owner approves the code, the server sends `ReceiveDisplayPaired` to this connection.
4. Store the token, then reconnect with it. The current connection stays unauthenticated.

Disconnecting before approval cancels the pending code, and disconnecting during approval removes the Display it would have created. Request a new code after reconnecting.

---

## Client-to-Server Methods (Invocations)

Clients invoke these methods on the hub:

### `SubscribeToUser(userId)`
Subscribes the connection to updates for the caller's own Session.
- **Parameters**: `userId` (`string?`): the caller's Session id. Ignored unless the connection's credential resolves to that same Session id.
- **Behavior**: Associates the connection with `user_{userId}` group in `SessionRegistry`. Upon subscription, the caller immediately receives initial snapshots of `ReceivePlaybackState`, `ReceiveLyrics`, `ReceiveTrackOffset`, and `ReceiveDiagnostics`. A no-op on a Display connection.

### `RegisterClientLogin(clientId)`
Subscribes the connection to an ephemeral client login channel.
- **Parameters**: `clientId` (`string`) — Temporary client handshake identifier.
- **Behavior**: Used by the desktop app to receive its owner Session following browser OAuth PKCE completion (`ReceiveAuthSession`). Ignored on a Display connection, since the handoff grants owner rights.

### `SyncClock(clientSendTimeMs)`
Initiates a 4-timestamp NTP clock sync sample.
- **Parameters**: `clientSendTimeMs` (`long`) — Client local UTC Unix epoch timestamp in milliseconds ($t_0$).
- **Returns / Broadcasts**: Returns or dispatches `ClockSyncResponse` containing $(t_0, t_1, t_2)$ in milliseconds.

### `SetTrackOffset(trackId, offsetMs)`
Stores a manual latency calibration offset for a specific track.
- **Parameters**:
  - `trackId` (`string`) — Spotify Track ID.
  - `offsetMs` (`int`) — Desired offset in milliseconds (e.g. `+150` or `-50`).
- **Behavior**: Persists the offset in SQLite and broadcasts `ReceiveTrackOffset` to the owner's Session connections and Displays. Requires a Session; ignored on a Display connection.

### `ReportClientVisibility(isVisible)`
Notifies the server whether the client window or browser tab is actively visible or hidden.
- **Parameters**: `isVisible` (`bool`) — `true` if active in viewport; `false` if minimized, hidden, or tabbed away.
- **Behavior**: Updates visibility state in `PlaybackSessionRegistry`. If `isVisible` is `true`, immediately requests user activity to wake up the polling engine without waiting for the scheduled background interval. On a Display connection the wake-up is throttled like `RefreshPlayback`.

### `RefreshPlayback()`
Requests an immediate on-demand Spotify playback refresh for the caller's subscribed user.
- **Behavior**: Invokes `IPlaybackSessionRegistry.RequestUserActivity`, cancelling the background poller's wait delay to query Spotify within milliseconds. Ideal for tab refocus or after manual playback operations.
- **Display connections**: Invokes `IPlaybackSessionRegistry.RequestDisplayActivity` instead, which forwards at most one refresh per owner every 5 seconds. Extra calls inside that window are dropped; the regular adaptive poll still runs.

---

## Server-to-Client Events (Broadcasts)

The server broadcasts these events to connected subscribers (`IPlaybackClient`):

### `ReceivePlaybackState`
Dispatched whenever track state changes (progress, play/pause, track change).

```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "trackTitle": "Never Gonna Give You Up",
  "artistName": "Rick Astley",
  "albumName": "Whenever You Need Somebody",
  "albumArtUrl": "https://i.scdn.co/image/ab67616d0000b273...",
  "durationMs": 213573,
  "progressMs": 45200,
  "isPlaying": true,
  "serverTimeUtc": "2026-09-01T01:15:00.000Z",
  "userId": "spotify_user_123",
  "displayName": "Rick"
}
```

### `ReceiveLyrics`
Dispatched when a new track starts playing or lyrics finish resolving.

```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
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
  ]
}
```

Each line has the same shape as in [`GET /api/lyrics/{trackId}`](rest-api.md#get-apilyricstrackid): `syllables` is `null` unless the line has real word timing.

### `ReceiveTrackOffset`
Dispatched when a user modifies or loads the latency offset for a track.

```json
{
  "trackId": "4cOdK2wGLETKBW3PvgPWqT",
  "offsetMs": 150
}
```

### `ReceiveClockSync`
Response payload for NTP round-trip offset and jitter computation.

```json
{
  "clientSendTimeMs": 1788225300000,
  "serverReceiveTimeMs": 1788225300015,
  "serverSendTimeMs": 1788225300016
}
```

### `ReceiveDiagnostics`
Periodic runtime telemetry broadcast.

```json
{
  "connectedClients": 2,
  "authorizedSessions": 1,
  "pollerStatus": "Active (Playing)",
  "activePollIntervalMs": 4000,
  "activeUserId": "spotify_user_123",
  "activeUserName": "Rick",
  "serverTimeUtc": "2026-09-01T01:15:00.000Z"
}
```

**`pollerStatus` Values**:
- `"Active (Playing)"`: User has active playback and at least one client tab is visible.
- `"Active (Background)"`: User has active playback but all connected client tabs are hidden/minimized.
- `"Paused"`: Playback is currently paused (cadence shifts from 5s up to 30s based on duration).
- `"Idle"`: No active playback detected on Spotify (cadence shifts from 10s up to 60s).
- `"Rate Limited (mm:ss)"`: Temporary cooldown after receiving an HTTP 429 from Spotify API.

### `ReceiveSessions` & `ReceiveAuthSession`
Broadcasts list of authorized Spotify accounts or notifies a newly authenticated login. Never sent to a Display connection.

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
Sent to each open connection of a Display right before the server closes it, because the owner revoked it. The payload is the Display id (`string`). The token no longer works, so the client should discard it and offer to pair again instead of reconnecting with it.
