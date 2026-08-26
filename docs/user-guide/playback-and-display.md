# Playback & Display Modes

Cantus is engineered to deliver a seamless, distraction-free lyrics display across any screen format—from a secondary desktop monitor to a dedicated living room TV kiosk.

---

## Interface Layout

The Cantus display interface is split into two harmonious zones designed for high legibility from across the room:

```mermaid
flowchart LR
    subgraph Now Playing Sidebar
        Art[Album Artwork]
        Title[Track Title]
        Artist[Artist & Album]
        Bar[Progress Indicator]
    end
    subgraph Real-Time Lyrics Viewport
        Past[Past Lyrics - Dimmed]
        Active[Active Singing Line - Highlighted & Enlarged]
        Upcoming[Upcoming Lyrics - Readable]
    end
    Now Playing Sidebar --- Real-Time Lyrics Viewport
```

1. **Now Playing Section**: Shows high-resolution album cover art, track title, artist name, and a smooth playback progress bar.
2. **Lyrics Viewport**: Displays vertically scrolling synchronized lyrics. The currently sung line is scaled, highlighted in high contrast with the theme's vibrant accent color, and centered on the screen.
3. **Instrumental Indicator**: When a song contains an extended instrumental section (> 8 seconds without lyrics), an ambient pulsing musical indicator appears so you know when vocals will resume.

---

## Display Environments

### 1. Smart TV & Kiosk Display

To use Cantus as an ambient music visualizer on a TV or dedicated Raspberry Pi display:

1. Open your browser on the smart TV (or launch a Chromium kiosk on Raspberry Pi) to your Cantus URL: `http://<your-server-ip>:5000`.
2. Select your user room from the room selector.
3. Enter fullscreen mode (press <kbd>F11</kbd> or use the TV browser's fullscreen button).
4. The cursor will auto-hide after 3 seconds of inactivity to keep the display clean.

> [!TIP]
> If using a Raspberry Pi or wall tablet, launch Chromium in kiosk mode:
> ```bash
> chromium-browser --kiosk --noerrdialogs --disable-infobars http://cantus.local:5000
> ```

---

### 2. Multi-Room & Shared Displays

Cantus supports multi-room subscriptions:
- Each authenticated Spotify account generates a dedicated **Room ID**.
- Any device on your local network (or the internet if reverse-proxied) can navigate to that Room URL.
- When you change tracks on your phone or Spotify desktop app, all connected room displays update simultaneously in real-time.

---

## Keyboard Shortcuts

When the Cantus display window is focused, you can use keyboard shortcuts for quick control:

| Key | Action | Description |
| :--- | :--- | :--- |
| <kbd>F11</kbd> / <kbd>F</kbd> | **Toggle Fullscreen** | Expands viewport to fill the entire monitor. |
| <kbd>[</kbd> | **Delay Lyrics (-50ms)** | Decreases timing offset if lyrics are appearing too early. |
| <kbd>]</kbd> | **Advance Lyrics (+50ms)** | Increases timing offset if lyrics are appearing too late. |
| <kbd>0</kbd> | **Reset Calibration** | Resets custom latency offset for the current track to `0ms`. |
| <kbd>D</kbd> | **Toggle Diagnostics HUD** | Shows real-time NTP roundtrip, clock skew, and SignalR latency. |
| <kbd>T</kbd> | **Toggle Theme Mode** | Cycles between Dynamic Cover Art, Dark Slate, and Light themes. |

---

## Diagnostics HUD

Pressing <kbd>D</kbd> opens the built-in Diagnostics HUD overlay. This displays live performance metrics useful for debugging latency and connection quality:

- **SignalR State**: Connection status (`Connected`, `Reconnecting`, `Disconnected`).
- **NTP Clock Offset**: Current estimated clock skew relative to the server (typically `< 2ms`).
- **Round-Trip Delay (RTT)**: WebSocket ping latency.
- **Active Polling Cadence**: Current server-side poll interval for your Spotify account (e.g. `500ms`).
- **Lyrics Source**: LRCLIB query result status (`Direct Hit`, `Fuzzy Match`, `Cache Hit`, `Instrumental`).
