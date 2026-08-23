# Server Engine & Real-Time Hub

The server engine is an ASP.NET Core 9 Minimal API host that manages authenticated Spotify playback sessions, runs intelligent background polling workers, and coordinates low-latency SignalR event distribution across connected display rooms.

## Layer Metadata

- **Layer ID**: `layer:server-api`
- **Component Count**: `49`
- **Role**: ASP.NET Core Minimal APIs, SignalR PlaybackHub, active background poller, and session registry.

## Key Components & Files

| Component | Type | Summary | Complexity |
| :--- | :---: | :--- | :---: |
| **`ActiveUsersPlaybackMonitor.cs`** | `file` | Background worker polling Spotify Web API for active users, detecting track transitions, and broadcasting playback state. | `complex` |
| **`ActiveUsersPlaybackMonitor`** | `class` | Class ActiveUsersPlaybackMonitor providing core functionality in ActiveUsersPlaybackMonitor.cs. | `complex` |
| **`PlaybackPollerOptions.cs`** | `file` | Source file: PlaybackPollerOptions.cs. | `simple` |
| **`AuthEndpoints.cs`** | `file` | Minimal API route endpoints for Spotify OAuth login redirect, callback exchange, session status, and revocation. | `moderate` |
| **`AuthEndpoints`** | `class` | Class AuthEndpoints providing core functionality in AuthEndpoints.cs. | `complex` |
| **`LyricsEndpoints.cs`** | `file` | Minimal API route endpoints for querying track lyrics and updating track latency offsets. | `moderate` |
| **`LyricsEndpoints`** | `class` | Class LyricsEndpoints providing core functionality in LyricsEndpoints.cs. | `moderate` |
| **`IPlaybackClient.cs`** | `file` | Source file: IPlaybackClient.cs. | `simple` |
| **`IPlaybackClient`** | `class` | Class IPlaybackClient providing core functionality in IPlaybackClient.cs. | `complex` |
| **`PlaybackHub.cs`** | `file` | SignalR hub managing real-time playback broadcasting, clock synchronization NTP round-trips, and room subscriptions. | `complex` |
| **`PlaybackHub`** | `class` | Class PlaybackHub providing core functionality in PlaybackHub.cs. | `complex` |
| **`AuthorizedSessionDto.cs`** | `file` | Data model / entity definition: AuthorizedSessionDto. | `simple` |
| **`ClockSyncDto.cs`** | `file` | Data model / entity definition: ClockSyncDto. | `simple` |
| **`DiagnosticsDto.cs`** | `file` | Data model / entity definition: DiagnosticsDto. | `simple` |
| **`DtoMappingExtensions.cs`** | `file` | Data model / entity definition: DtoMappingExtensions. | `simple` |
| **`DtoMappingExtensions`** | `class` | Class DtoMappingExtensions providing core functionality in DtoMappingExtensions.cs. | `moderate` |
| **`LyricLineDto.cs`** | `file` | Data model / entity definition: LyricLineDto. | `simple` |
| **`LyricsDto.cs`** | `file` | Data model / entity definition: LyricsDto. | `simple` |
| **`PlaybackStateDto.cs`** | `file` | Data model / entity definition: PlaybackStateDto. | `simple` |
| **`TrackInfoDto.cs`** | `file` | Data model / entity definition: TrackInfoDto. | `simple` |
| **`TrackOffsetDto.cs`** | `file` | Data model / entity definition: TrackOffsetDto. | `simple` |
| **`Program.cs`** | `file` | Server bootstrap entry point configuring ASP.NET Core DI container, middleware pipeline, SignalR hubs, and endpoints. | `complex` |
| **`HostUrlResolver.cs`** | `file` | Dynamically resolves server host base URL and Spotify redirect URI from HTTP headers and configuration. | `moderate` |
| **`HostUrlResolver`** | `class` | Class HostUrlResolver providing core functionality in HostUrlResolver.cs. | `moderate` |
| **`IHostUrlResolver.cs`** | `file` | Dynamically resolves server host base URL and Spotify redirect URI from HTTP headers and configuration. | `moderate` |
| **`IHostUrlResolver`** | `class` | Class IHostUrlResolver providing core functionality in IHostUrlResolver.cs. | `moderate` |
| **`IPlaybackSessionRegistry.cs`** | `file` | Thread-safe in-memory session registry tracking connected SignalR clients, subscriptions, and active user playback states. | `complex` |
| **`IPlaybackSessionRegistry`** | `class` | Class IPlaybackSessionRegistry providing core functionality in IPlaybackSessionRegistry.cs. | `complex` |
| **`PkceHelper.cs`** | `file` | Source file: PkceHelper.cs. | `simple` |
| **`PkceHelper`** | `class` | Class PkceHelper providing core functionality in PkceHelper.cs. | `moderate` |
| **`PlaybackSessionRegistry.cs`** | `file` | Thread-safe in-memory session registry tracking connected SignalR clients, subscriptions, and active user playback states. | `complex` |
| **`PlaybackSessionRegistry`** | `class` | Class PlaybackSessionRegistry providing core functionality in PlaybackSessionRegistry.cs. | `complex` |

## Member Functions & Endpoints

| Symbol | Summary | Tags |
| :--- | :--- | :--- |
| **`ActiveUsersPlaybackMonitor`** | Method/function ActiveUsersPlaybackMonitor in ActiveUsersPlaybackMonitor.cs. | `function`, `method` |
| **`ExecuteAsync`** | Method/function ExecuteAsync in ActiveUsersPlaybackMonitor.cs. | `function`, `method` |
| **`PollActiveSessionsAsync`** | Method/function PollActiveSessionsAsync in ActiveUsersPlaybackMonitor.cs. | `function`, `method` |
| **`BroadcastDiagnosticsAsync`** | Method/function BroadcastDiagnosticsAsync in ActiveUsersPlaybackMonitor.cs. | `function`, `method` |
| **`MapAuthEndpoints`** | Method/function MapAuthEndpoints in AuthEndpoints.cs. | `function`, `method` |
| **`MapLyricsEndpoints`** | Method/function MapLyricsEndpoints in LyricsEndpoints.cs. | `function`, `method` |
| **`PlaybackHub`** | Method/function PlaybackHub in PlaybackHub.cs. | `function`, `method` |
| **`OnConnectedAsync`** | Method/function OnConnectedAsync in PlaybackHub.cs. | `function`, `method` |
| **`SubscribeToUser`** | Method/function SubscribeToUser in PlaybackHub.cs. | `function`, `method` |
| **`SetTrackOffset`** | Method/function SetTrackOffset in PlaybackHub.cs. | `function`, `method` |
| **`ToDto`** | Method/function ToDto in DtoMappingExtensions.cs. | `function`, `method` |
| **`ResolveBaseUrl`** | Method/function ResolveBaseUrl in HostUrlResolver.cs. | `function`, `method` |
| **`ResolveSpotifyRedirectUri`** | Method/function ResolveSpotifyRedirectUri in HostUrlResolver.cs. | `function`, `method` |
| **`RegisterConnection`** | Method/function RegisterConnection in PlaybackSessionRegistry.cs. | `function`, `method` |
| **`UnregisterConnection`** | Method/function UnregisterConnection in PlaybackSessionRegistry.cs. | `function`, `method` |
| **`UpdateUserState`** | Method/function UpdateUserState in PlaybackSessionRegistry.cs. | `function`, `method` |
| **`GetActivePlaybackSnapshot`** | Method/function GetActivePlaybackSnapshot in PlaybackSessionRegistry.cs. | `function`, `method` |
