# Core Domain Contracts & Models

The core domain layer encapsulates framework-agnostic models, interfaces, and LRC parsing algorithms. It defines the contracts that govern playback state snapshots, synchronized lyric timings, provider abstractions, and clock offset calculations.

## Layer Metadata

- **Layer ID**: `layer:core-domain`
- **Component Count**: `20`
- **Role**: Domain models (PlaybackState, SyncedLyrics, LyricLine), parser algorithms (LrcParser), and repository interfaces.

## Key Components & Files

| Component | Type | Summary | Complexity |
| :--- | :---: | :--- | :---: |
| **`ILyricsCacheRepository.cs`** | `file` | Contract interface definition: ILyricsCacheRepository. | `simple` |
| **`ILyricsCacheRepository`** | `class` | Class ILyricsCacheRepository providing core functionality in ILyricsCacheRepository.cs. | `complex` |
| **`ILyricsProvider.cs`** | `file` | Contract interface definition: ILyricsProvider. | `simple` |
| **`IPlaybackInterpolator.cs`** | `file` | Sub-millisecond playback position clock interpolator accounting for network latency, server clock skew, and drift. | `complex` |
| **`IPlaybackInterpolator`** | `class` | Class IPlaybackInterpolator providing core functionality in IPlaybackInterpolator.cs. | `moderate` |
| **`ISpotifyAuthService.cs`** | `file` | Spotify OAuth PKCE authentication service handling code exchange, token encryption, and refresh loops. | `complex` |
| **`ISpotifyAuthService`** | `class` | Class ISpotifyAuthService providing core functionality in ISpotifyAuthService.cs. | `complex` |
| **`ISpotifyPlayerClient.cs`** | `file` | Client implementation for querying current user playback state via Spotify Web API. | `moderate` |
| **`LyricLine.cs`** | `file` | Data model / entity definition: LyricLine. | `simple` |
| **`PlaybackState.cs`** | `file` | Data model / entity definition: PlaybackState. | `simple` |
| **`SyncedLyrics.cs`** | `file` | Data model / entity definition: SyncedLyrics. | `simple` |
| **`SyncedLyrics`** | `class` | Class SyncedLyrics providing core functionality in SyncedLyrics.cs. | `moderate` |
| **`TrackInfo.cs`** | `file` | Data model / entity definition: TrackInfo. | `simple` |
| **`UserSession.cs`** | `file` | Data model / entity definition: UserSession. | `simple` |
| **`LrcParser.cs`** | `file` | High-performance parser for LRC timestamped format, syllable metadata, and offset adjustment. | `moderate` |
| **`LrcParser`** | `class` | Class LrcParser providing core functionality in LrcParser.cs. | `complex` |

## Member Functions & Endpoints

| Symbol | Summary | Tags |
| :--- | :--- | :--- |
| **`GetActiveLineIndex`** | Method/function GetActiveLineIndex in SyncedLyrics.cs. | `function`, `method` |
| **`Parse`** | Method/function Parse in LrcParser.cs. | `function`, `method` |
| **`ParseLines`** | Method/function ParseLines in LrcParser.cs. | `function`, `method` |
| **`TryParseTimestamp`** | Method/function TryParseTimestamp in LrcParser.cs. | `function`, `method` |
