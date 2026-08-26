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

### `JoinRoom(userId)`
Subscribes the connection to updates for a specific Spotify user ID room.
- **Parameters**: `userId` (`string`) — Spotify User Account identifier.
- **Behavior**: Increments active viewer count in `SessionRegistry`. If the account was sleeping, resumes active Spotify polling immediately.

### `LeaveRoom(userId)`
Unsubscribes the connection from a room.
- **Parameters**: `userId` (`string`) — Spotify User Account identifier.
- **Behavior**: Decrements active viewer count. If viewer count reaches 0, halts background polling for that account.

### `SyncClock(clientSendTimeTicks)`
Initiates a 4-timestamp NTP clock sync sample.
- **Parameters**: `clientSendTimeTicks` (`long`) — Client local UTC time in .NET Ticks ($t_0$).
- **Response**: Triggers `SyncClockResponse` event back to the calling client.

### `CalibrateOffset(trackId, offsetMs)`
Stores a manual latency calibration offset for a specific track.
- **Parameters**:
  - `trackId` (`string`) — Spotify Track ID.
  - `offsetMs` (`int`) — Desired offset in milliseconds (e.g. `+150` or `-50`).

---

## Server-to-Client Events (Broadcasts)

The server broadcasts these events to connected room subscribers:

### `ReceivePlaybackState`
Dispatched whenever track state changes (progress, play/pause, volume, new track).

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
  "serverTimestampUtcTicks": 638600000000000000
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

### `SyncClockResponse`
Direct response to a client `SyncClock` invocation for NTP offset computation.

```json
{
  "t0": 638600000000100000,
  "t1": 638600000000115000,
  "t2": 638600000000116000
}
```
