# Real-Time Playback Monitoring & SignalR Broadcast

Manages intelligent adaptive background polling of active Spotify player instances, tracks track transitions and play/pause states, and coordinates broadcasts across SignalR clients.

## Domain Rules & Constraints

- **Dynamic polling cadence: 500ms playing, 3000ms paused, 10000ms idle**
- **Polling is halted when no SignalR clients are subscribed to conserve Spotify rate limits**
- **Automatic lyric prefetching triggered on track transitions**

## Key Domain Entities

| Entity | Description |
| :--- | :--- |
| **`PlaybackState`** | Core domain entity representing state within Real-Time Playback Monitoring & Polling |
| **`TrackInfo`** | Core domain entity representing state within Real-Time Playback Monitoring & Polling |
| **`UserPlaybackSnapshot`** | Core domain entity representing state within Real-Time Playback Monitoring & Polling |