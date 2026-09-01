# SignalR PlaybackHub Protocol Reference

The Cantus real-time streaming protocol is implemented over **ASP.NET Core SignalR**. It powers instant state synchronization, low-latency lyric broadcasting, and 4-timestamp NTP clock sync.

---

## Hub Endpoint

```http
WSS /hubs/playback
```

Clients negotiate connection parameters over HTTP and upgrade to WebSockets.

---

## Client-to-Server Methods (Invocations)

Clients invoke these methods on the hub:

### `SubscribeToUser(userId)`
Subscribes the connection to updates for a specific Spotify user ID.
- **Parameters**: `userId` (`string?`) — Spotify User Account identifier.
- **Behavior**: Associates the connection with `user_{userId}` group in `SessionRegistry`. Upon subscription, the caller immediately receives initial snapshots of `ReceivePlaybackState`, `ReceiveLyrics`, `ReceiveTrackOffset`, and `ReceiveDiagnostics`.

### `RegisterClientLogin(clientId)`
Subscribes the connection to an ephemeral client login channel.
- **Parameters**: `clientId` (`string`) — Temporary client handshake identifier.
- **Behavior**: Used by desktop and remote displays to receive authenticated session tokens following browser OAuth PKCE completion (`ReceiveAuthSession`).

### `SyncClock(clientSendTimeMs)`
Initiates a 4-timestamp NTP clock sync sample.
- **Parameters**: `clientSendTimeMs` (`long`) — Client local UTC Unix epoch timestamp in milliseconds ($t_0$).
- **Returns / Broadcasts**: Returns or dispatches `ClockSyncResponse` containing $(t_0, t_1, t_2)$ in milliseconds.

### `SetTrackOffset(trackId, offsetMs)`
Stores a manual latency calibration offset for a specific track.
- **Parameters**:
  - `trackId` (`string`) — Spotify Track ID.
  - `offsetMs` (`int`) — Desired offset in milliseconds (e.g. `+150` or `-50`).
- **Behavior**: Persists the offset in SQLite and broadcasts `ReceiveTrackOffset` to all active viewers in the user group.

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
      "startTimeMs": 18450,
      "endTimeMs": 22300,
      "text": "We're no strangers to love"
    },
    {
      "startTimeMs": 22800,
      "endTimeMs": 26900,
      "text": "You know the rules and so do I"
    }
  ]
}
```

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
  "activeUserId": "spotify_user_123",
  "activeUserName": "Rick",
  "serverTimeUtc": "2026-09-01T01:15:00.000Z"
}
```

### `ReceiveSessions` & `ReceiveAuthSession`
Broadcasts list of authorized Spotify accounts or notifies a newly authenticated login.
