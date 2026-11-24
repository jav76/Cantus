# Dynamic Theming & Visuals

Cantus features a dynamic theming engine that bridges the visual mood of your music with the lyric display.

---

## Dynamic Album Art Palette Extraction

When a new track starts playing with the **Dynamic Palette** theme, Cantus downloads the album artwork directly in the client and extracts a harmonized color palette from its pixels:

```mermaid
flowchart TD
    Art[Spotify Album Artwork] --> Fetch[AlbumArtColorService Fetch + Decode]
    Fetch --> Quantize[ColorQuantizer Median-Cut Quantization]
    Quantize --> Swatches[Dominant Color Swatches]
    Swatches --> Primary[Vibrant Primary Accent]
    Swatches --> Secondary[Hue-Separated Secondary Accent]
    Secondary --> Karaoke[Karaoke Line-Progress Underline]
    Swatches --> Background[Hue-Matched Dark Background]
    Primary --> ActiveLyric[Active Lyric Glow and Accents]
    Art --> Backdrop[Full-Screen Artwork Backdrop Behind Darkening Scrim]
    Fetch --> Blur[Optional Blurred Backdrop]
```

### Color Extraction Principles

1. **Median-Cut Quantization**: The artwork is downsampled and quantized into up to eight dominant color swatches. Near-black, near-white, and transparent pixels are excluded so letterboxing and vignettes do not skew the palette.
2. **Vibrancy-Weighted Accent Selection**: The primary accent is the swatch with the best combination of saturation, population, and mid-range lightness. The secondary accent prefers a swatch at least 30 degrees away in hue; grayscale artwork keeps its neutral character instead of being forced into artificial color.
3. **Legibility Clamping**: Accent saturation and lightness are clamped into ranges that keep lyrics crisp against the hue-matched dark background, regardless of how dark or washed out the artwork is.
4. **Ambient Artwork Backdrop**: The full-resolution album cover fills the entire viewport behind a darkening scrim (center-cropped to fit), so different albums produce visibly different backdrops. The lyrics sit straight on this backdrop with no panel behind them, so the scrim is dark enough to keep them readable even on a bright cover. With **Blur album background** on, a softly blurred copy replaces the sharp cover (see [Post-Processing Effects](#post-processing-effects)).
5. **Instant Fallback**: A metadata-derived placeholder palette is applied immediately while extraction runs, and remains active if the artwork cannot be fetched (for example, when offline). The palettes of the last 16 artworks are cached in memory, so revisiting a recent track re-themes instantly.

---

## Available Theme Modes

Pick a theme from either dropdown:

- **Header bar:** the **Theme** button (a palette icon with a small arrow on tablet layouts) opens a menu that lists every theme, with a check next to the active one. On small screens the same list is under **Theme** in the overflow menu.
- **Sync & Info tab (mobile):** the **Color theme** section has a dropdown of all themes.

<kbd>T</kbd> still moves to the next theme in list order: Dynamic Palette, Midnight Violet, Emerald Synth, Cyberpunk Sunset, Nordic Slate, OLED Monochrome, Solarized Dark, then back to Dynamic Palette. Every change shows a short toast with the theme name. The theme choice is saved per device, so the next launch opens on it. A device that has never chosen a theme starts on Dynamic Palette.

| Theme Mode | Description | Ideal Use Case |
| :--- | :--- | :--- |
| **Dynamic Palette (Default)** | Adapts colors, glow, and text highlights to match the current album artwork. | Daily listening, TV living room display, visualizers. |
| **Midnight Violet** | Deep indigo background with violet accents. | General use, low-light viewing. |
| **Emerald Synth** | Dark slate background with emerald green accents. | Night viewing, low eye strain. |
| **Cyberpunk Sunset** | Dark purple-tinted background with neon rose and amber accents. | High-energy visuals. |
| **Nordic Slate** | Deep slate-navy background with cyan and sky-blue accents. | Subtle, understated displays. |
| **OLED Monochrome** | True-black background with white accents. | OLED displays, burn-in avoidance. |
| **Solarized Dark** | Classic Solarized base tones with blue accents. | Long reading sessions. |

Every theme is dark, and the whole interface follows it: text boxes, menus, dropdowns, checkboxes, progress rings and dialogs (Find a song, Wrong lyrics?, Diagnostics) all switch colours with the theme, without a restart. Labels on accent-coloured buttons and the selected mobile tab turn dark when the accent is light (for example the white accent of OLED Monochrome). With Dynamic Palette, they use whichever of dark or white contrasts more with the album accent, so amber, orange and lime accents get dark labels.

---

## Post-Processing Effects

Optional effects you can combine freely. **Film grain**, **Blur album background** and **Seek bar VU meter** are on by default, **Vignette** and **Scanlines** are off. A device paired as a [Display](displays.md) starts with film grain off to save power on TVs and small boards, until you switch it on there.

| Effect | What it does | Applies to |
| :--- | :--- | :--- |
| **Film grain** | Faint animated noise, like old film. | Every theme and kiosk mode |
| **Vignette** | Darkens the window edges and leaves the centre clear. | Every theme and kiosk mode |
| **Scanlines** | Thin, static horizontal lines, like a CRT screen. | Every theme and kiosk mode |
| **Blur album background** | Shows a soft, blurred version of the album cover behind the lyrics instead of the sharp one, with a slightly lighter scrim. | The **Dynamic Palette** theme only |
| **Seek bar VU meter** | Level bars above and below the seek bar, low to high frequency from left to right across the part of the track that has played. A bar only shows while its frequency is active. | The track card and the kiosk bar |

- Toggle them in the theme menu, below the list of themes (on small screens: overflow menu, **Theme**), or with the checkboxes under **Effects** in the **Color theme** section on the mobile **Sync & Info** tab.
- Each effect is saved per device (browser or desktop install), like the Karaoke toggle, and is not synced to other devices. Once you switch an effect on or off, your choice is kept even if the defaults change in a later release.
- **Blur album background** (labelled **Blur album background (Dynamic theme)** in the theme menu) can be switched on in any theme, but it only changes something in Dynamic Palette, the one theme with an album art backdrop. The sharp cover shows first and the blurred one replaces it a moment later. If the blur fails, the sharp cover stays.
- All effects are subtle, so lyrics stay readable. The grain animation stops while film grain is off.

### Seek Bar VU Meter

The bars use the theme's accent colour. They fall flat when playback pauses and hold still while the pointer is over a seek bar you can click, so it is clear the bar is a control. **VU meter levels** (theme menu, or the picker under **Effects** on the mobile **Sync & Info** tab) picks where the levels come from. The first line of that submenu, and the text under the picker, says whether the bars are live right now.

| Levels | Real audio? | Where | Notes |
| :--- | :--- | :--- | :--- |
| **System audio** | Yes | Desktop app on Linux and Windows | The default there. Hears everything the default output plays, not just the player, and only when the music plays on this computer. Linux needs `parec` (PulseAudio or PipeWire) or `pw-record`. Re-pick it after switching outputs. |
| **Microphone** | Yes | Browser | Hears the room, so a display reacts to speakers playing Spotify anywhere nearby. Asks for microphone access. |
| **Tab or screen audio** | Yes | Browser (Chrome and Edge) | Pick a tab, or the whole screen on Windows and ChromeOS, and tick **Share audio**. Asks every time, so pick it again after a reload or after you stop sharing. |
| **Decorative** | No | Everywhere | A beat-like pattern seeded by the track. It follows the playback position but not the music. The browser default. |

- Spotify does not offer audio data to Cantus, so with Spotify playing on another device only **Microphone** gives real levels.
- When a live source hears nothing for a moment (nothing playing, or the music plays elsewhere), the decorative pattern takes over until sound returns.
- Browsers only allow microphone and screen capture on HTTPS or `localhost`. Over plain HTTP on your network, only **Decorative** is offered.
- Audio never leaves the device: it is analysed locally and nothing is sent to the server.

### Where settings are saved

The theme, effects, Karaoke, auto-scroll and device latency are kept on the device, never on the server:

- **Web app:** in the browser's local storage for the Cantus address you opened. A different browser, a private window, or a different server address or port starts with the defaults. Clearing the site's data resets them.
- **Desktop app:** in the app data folder of your OS user (`~/.local/share/Cantus.Client/Settings` on Linux).

---

## Lyric Animation States

Each line of synchronized lyrics is in one of three visual states:

```mermaid
stateDiagram-v2
    [*] --> Upcoming: Track loaded
    Upcoming --> Active: Track time reaches line timestamp
    Active --> Passed: Next line becomes active
    Passed --> [*]: Song finished
```

- **Upcoming Lines**: 75% opacity, regular weight, in the theme's upcoming-lyric color.
- **Active Line**: 100% opacity, semibold, larger font, in the theme's active-lyric color, centered vertically in the viewport.
- **Passed Lines**: 45% opacity, regular weight, smaller font, scrolled up out of the center.

### Instrumental Breaks

When the gap from the active line to the next one is 8 seconds or more (or before the first line), and at least 3 seconds remain, Cantus shows a countdown such as `♪ Instrumental Interlude (12s) ♪` on the lyrics stage and the track card. It is hidden in static lyrics mode.
