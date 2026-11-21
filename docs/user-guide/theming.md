# Dynamic Theming & Visuals

Cantus features a dynamic theming engine that bridges the visual mood of your music with the lyric display.

---

## Dynamic Album Art Palette Extraction

When a new track starts playing in **Dynamic** mode, Cantus downloads the album artwork directly in the client and extracts a harmonized color palette from its pixels:

```mermaid
flowchart TD
    Art[Spotify Album Artwork] --> Fetch[AlbumArtColorService Fetch + Decode]
    Fetch --> Quantize[ColorQuantizer Median-Cut Quantization]
    Quantize --> Swatches[Dominant Color Swatches]
    Swatches --> Primary[Vibrant Primary Accent]
    Swatches --> Secondary[Hue-Separated Secondary Accent]
    Swatches --> Background[Hue-Matched Dark Background]
    Primary --> ActiveLyric[Active Lyric Glow and Accents]
    Art --> Backdrop[Full-Screen Artwork Backdrop Behind Darkening Scrim]
```

### Color Extraction Principles

1. **Median-Cut Quantization**: The artwork is downsampled and quantized into up to eight dominant color swatches. Near-black, near-white, and transparent pixels are excluded so letterboxing and vignettes do not skew the palette.
2. **Vibrancy-Weighted Accent Selection**: The primary accent is the swatch with the best combination of saturation, population, and mid-range lightness. The secondary accent prefers a swatch at least 30 degrees away in hue; grayscale artwork keeps its neutral character instead of being forced into artificial color.
3. **Legibility Clamping**: Accent saturation and lightness are clamped into ranges that keep lyrics crisp against the hue-matched dark background, regardless of how dark or washed out the artwork is.
4. **Ambient Artwork Backdrop**: The full-resolution album cover fills the entire viewport behind a darkening scrim (center-cropped to fit), so different albums produce visibly different backdrops: recognizable artwork in the background, with the scrim keeping lyrics comfortably readable on top.
5. **Instant Fallback**: A metadata-derived placeholder palette is applied immediately while extraction runs, and remains active if the artwork cannot be fetched (for example, when offline). Extracted palettes are cached per artwork URL, so revisiting a track re-themes instantly.

---

## Available Theme Modes

Pick a theme from either dropdown:

- **Header bar:** the **Theme** button (a palette icon with a small arrow on tablet layouts) opens a menu that lists every theme, with a check next to the active one. On small screens the same list is under **Theme** in the overflow menu.
- **Sync & Info tab (mobile):** the **COLOR THEME** card has a dropdown of all themes.

<kbd>T</kbd> still moves to the next theme in list order: Dynamic, Midnight Violet, Emerald Synth, Cyberpunk Sunset, Nordic Slate, OLED Monochrome, Solarized Dark, then back to Dynamic. Every change shows a short toast with the theme name. The theme choice is not saved: each launch starts on Midnight Violet.

| Theme Mode | Description | Ideal Use Case |
| :--- | :--- | :--- |
| **Dynamic** | Adapts colors, glow, and text highlights to match the current album artwork. | Daily listening, TV living room display, visualizers. |
| **Midnight Violet (Default)** | Deep indigo background with violet accents. | General use, low-light viewing. |
| **Emerald Synth** | Dark slate background with emerald green accents. | Night viewing, low eye strain. |
| **Cyberpunk Sunset** | Dark purple-tinted background with neon rose and amber accents. | High-energy visuals. |
| **Nordic Slate** | Deep slate-navy background with cyan and sky-blue accents. | Subtle, understated displays. |
| **OLED Monochrome** | True-black background with white accents. | OLED displays, burn-in avoidance. |
| **Solarized Dark** | Classic Solarized base tones with blue accents. | Long reading sessions. |

Every theme is dark, and the whole interface follows it: text boxes, menus, dropdowns, checkboxes, progress rings and dialogs (Find a song, Wrong lyrics?, Diagnostics) all switch colours with the theme, without a restart. Labels on accent-coloured buttons turn dark when the accent is light (for example the white accent of OLED Monochrome), so they stay readable.

---

## Post-Processing Effects

**Post-processing effects** adds animated film grain and a soft vignette (darker edges) over the whole app, on top of every theme and in kiosk mode. It is off by default.

- Turn it on or off with the **Post-processing effects** item at the bottom of the theme menu, or the checkbox in the **COLOR THEME** card on the mobile **Sync & Info** tab.
- The setting is saved per device (browser or desktop install), like the Karaoke toggle, and is not synced to other devices.
- The grain is faint and the vignette leaves the centre clear, so lyrics stay readable. The animation stops while the effects are off.

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

- **Upcoming Lines**: 75% opacity, medium weight, in the theme's upcoming-lyric color.
- **Active Line**: 100% opacity, bold, larger font, in the theme's active-lyric color, centered vertically in the viewport.
- **Passed Lines**: 45% opacity, normal weight, smaller font, scrolled up out of the center.

### Instrumental Breaks

When the gap from the active line to the next one is 8 seconds or more (or before the first line), and at least 3 seconds remain, Cantus shows a countdown such as `♪ Instrumental Interlude (12s) ♪` on the lyrics stage and the track card. It is hidden in static lyrics mode.
