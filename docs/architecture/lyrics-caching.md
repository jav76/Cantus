# Multi-Tier Lyrics Caching

Lyrics retrieval in Cantus is designed for instantaneous local response times and resilient fallback handling across large, diverse music catalogs.

---

## Retrieval Resolution Strategy

When a track starts playing, Cantus traverses a multi-tiered caching pipeline before querying external APIs:

```mermaid
flowchart TD
    Start[Track Changed / New Track ID] --> QueryLocal{1. Query SQLite Cache}
    QueryLocal -->|Found Positive LRC| ReturnLyrics[Return Parsed Lyric Lines]
    QueryLocal -->|Found Negative Cache| ReturnInstrumental[Flag as Instrumental / No Lyrics]
    
    QueryLocal -->|Cache Miss| QueryLRCLIB[2. Query LRCLIB Synced API]
    
    QueryLRCLIB -->|Exact Match Found| SavePositive[3. Save to SQLite: 30-Day TTL]
    QueryLRCLIB -->|Fuzzy Match Found| SavePositive
    QueryLRCLIB -->|No Synced Lyrics / Instrumental| SaveNegative[4. Save Negative Cache: 7-Day TTL]
    
    SavePositive --> ReturnLyrics
    SaveNegative --> ReturnInstrumental
```

---

## Multi-Tier Cache Features

### 1. SQLite Local Positive Cache
- **Duration**: 30 Days (auto-renewing on access).
- **Storage**: Raw LRC string and normalized JSON parsed line objects indexed by Spotify Track ID and Artist/Title hash.
- **Latency**: `< 1ms` retrieval from local disk.

### 2. Negative Caching for Instrumental Tracks
- **Problem**: Many classical, jazz, EDM, and post-rock tracks have no lyrics. Without caching this absence, the server would query LRCLIB on every track transition.
- **Solution**: Cantus records a **Negative Cache** entry with a 7-day TTL. When the track plays again, Cantus immediately recognizes it as instrumental without network queries.

### 3. LRCLIB Integration & Fuzzy Matching
- **Primary Query**: Exact search by Track Name, Artist Name, Album Name, and Duration.
- **Fuzzy Fallback**: If exact matching fails (common for remastered titles, "feat." artist tags, or deluxe editions), Cantus strips noise keywords (e.g. `(Remastered 2021)`, `[Deluxe Edition]`) and queries LRCLIB's fuzzy search endpoint.

---

## LRC Parsing & Timestamp Normalization

Cantus implements a zero-allocation LRC parser (`LrcParser`) that translates standard LRC text formats into typed timestamp objects:

```
[00:12.45]First line of synchronized lyrics
[00:15.80]Second line of synchronized lyrics
[00:22.10]Third line after a brief break
```

### Parser Features:
- Handles both 2-digit (`[mm:ss.xx]`) and 3-digit (`[mm:ss.xxx]`) millisecond precision.
- Normalizes out-of-order lyric lines.
- Computes start and end durations for each line to trigger smooth visual highlighting and transition states.
