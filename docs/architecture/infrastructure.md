# Infrastructure & Persistence Layer

The infrastructure layer handles external integrations and persistence: Spotify OAuth PKCE token exchange, EF Core SQLite caching with negative cache tracking, LRCLIB API queries with fuzzy matching, token encryption at rest via ASP.NET Core Data Protection, and sub-millisecond playback clock interpolation.

## Layer Metadata

- **Layer ID**: `layer:infrastructure-persistence`
- **Component Count**: `64`
- **Role**: Spotify OAuth/Web API clients, LRCLIB lyrics provider, SQLite EF Core database persistence, and token encryption.

## Key Components & Files

| Component | Type | Summary | Complexity |
| :--- | :---: | :--- | :---: |
| **`PlaybackInterpolator.cs`** | `file` | Sub-millisecond playback position clock interpolator accounting for network latency, server clock skew, and drift. | `complex` |
| **`PlaybackInterpolator`** | `class` | Class PlaybackInterpolator providing core functionality in PlaybackInterpolator.cs. | `complex` |
| **`PlaybackInterpolatorOptions.cs`** | `file` | Source file: PlaybackInterpolatorOptions.cs. | `simple` |
| **`DependencyInjection.cs`** | `file` | Source file: DependencyInjection.cs. | `simple` |
| **`DependencyInjection`** | `class` | Class DependencyInjection providing core functionality in DependencyInjection.cs. | `moderate` |
| **`CachedLyricsService.cs`** | `file` | Lyrics coordination service checking local SQLite cache before fetching external lyrics from LRCLIB. | `moderate` |
| **`CachedLyricsService`** | `class` | Class CachedLyricsService providing core functionality in CachedLyricsService.cs. | `moderate` |
| **`LrclibLyricsProvider.cs`** | `file` | Provider fetching synchronized and plain lyrics from LRCLIB API with title/artist fuzzy fallback matching. | `complex` |
| **`LrclibLyricsProvider`** | `class` | Class LrclibLyricsProvider providing core functionality in LrclibLyricsProvider.cs. | `complex` |
| **`LrclibOptions.cs`** | `file` | Source file: LrclibOptions.cs. | `simple` |
| **`SqliteLyricsCacheRepository.cs`** | `file` | SQLite EF Core repository caching synced and plain lyrics, negative lookups, and per-track latency offsets. | `complex` |
| **`SqliteLyricsCacheRepository`** | `class` | Class SqliteLyricsCacheRepository providing core functionality in SqliteLyricsCacheRepository.cs. | `complex` |
| **`20260823130609_InitialCreate.Designer.cs`** | `file` | Source file: 20260823130609_InitialCreate.Designer.cs. | `simple` |
| **`InitialCreate`** | `class` | Class InitialCreate providing core functionality in 20260823130609_InitialCreate.Designer.cs. | `complex` |
| **`20260823130609_InitialCreate.cs`** | `file` | Source file: 20260823130609_InitialCreate.cs. | `simple` |
| **`InitialCreate`** | `class` | Class InitialCreate providing core functionality in 20260823130609_InitialCreate.cs. | `complex` |
| **`CantusDbContextModelSnapshot.cs`** | `file` | Source file: CantusDbContextModelSnapshot.cs. | `simple` |
| **`CantusDbContextModelSnapshot`** | `class` | Class CantusDbContextModelSnapshot providing core functionality in CantusDbContextModelSnapshot.cs. | `complex` |
| **`CantusDbContext.cs`** | `file` | Entity Framework Core database context for UserSessions, CachedLyrics, TrackOffsets, and Rooms. | `moderate` |
| **`CantusDbContext`** | `class` | Class CantusDbContext providing core functionality in CantusDbContext.cs. | `moderate` |
| **`CantusDbContextFactory.cs`** | `file` | Source file: CantusDbContextFactory.cs. | `simple` |
| **`CachedLyricsEntity.cs`** | `file` | Data model / entity definition: CachedLyricsEntity. | `simple` |
| **`CachedLyricsEntity`** | `class` | Class CachedLyricsEntity providing core functionality in CachedLyricsEntity.cs. | `moderate` |
| **`RoomEntity.cs`** | `file` | Data model / entity definition: RoomEntity. | `simple` |
| **`TrackOffsetEntity.cs`** | `file` | Data model / entity definition: TrackOffsetEntity. | `simple` |
| **`UserSessionEntity.cs`** | `file` | Data model / entity definition: UserSessionEntity. | `simple` |
| **`DataProtectionTokenEncryptionService.cs`** | `file` | Encryption service securing OAuth refresh tokens at rest using ASP.NET Core Data Protection. | `moderate` |
| **`DataProtectionTokenEncryptionService`** | `class` | Class DataProtectionTokenEncryptionService providing core functionality in DataProtectionTokenEncryptionService.cs. | `moderate` |
| **`ITokenEncryptionService.cs`** | `file` | Source file: ITokenEncryptionService.cs. | `simple` |
| **`ITokenEncryptionService`** | `class` | Class ITokenEncryptionService providing core functionality in ITokenEncryptionService.cs. | `moderate` |
| **`SpotifyAuthService.cs`** | `file` | Spotify OAuth PKCE authentication service handling code exchange, token encryption, and refresh loops. | `complex` |
| **`SpotifyAuthService`** | `class` | Class SpotifyAuthService providing core functionality in SpotifyAuthService.cs. | `complex` |
| **`SpotifyOptions.cs`** | `file` | Source file: SpotifyOptions.cs. | `simple` |
| **`SpotifyPlayerClient.cs`** | `file` | Client implementation for querying current user playback state via Spotify Web API. | `moderate` |
| **`SpotifyPlayerClient`** | `class` | Class SpotifyPlayerClient providing core functionality in SpotifyPlayerClient.cs. | `moderate` |

## Member Functions & Endpoints

| Symbol | Summary | Tags |
| :--- | :--- | :--- |
| **`CalculateCurrentPosition`** | Method/function CalculateCurrentPosition in PlaybackInterpolator.cs. | `function`, `method` |
| **`AddCantusInfrastructure`** | Method/function AddCantusInfrastructure in DependencyInjection.cs. | `function`, `method` |
| **`CachedLyricsService`** | Method/function CachedLyricsService in CachedLyricsService.cs. | `function`, `method` |
| **`GetLyricsAsync`** | Method/function GetLyricsAsync in CachedLyricsService.cs. | `function`, `method` |
| **`LrclibLyricsProvider`** | Method/function LrclibLyricsProvider in LrclibLyricsProvider.cs. | `function`, `method` |
| **`GetLyricsAsync`** | Method/function GetLyricsAsync in LrclibLyricsProvider.cs. | `function`, `method` |
| **`TryGetExactLyricsAsync`** | Method/function TryGetExactLyricsAsync in LrclibLyricsProvider.cs. | `function`, `method` |
| **`TrySearchLyricsAsync`** | Method/function TrySearchLyricsAsync in LrclibLyricsProvider.cs. | `function`, `method` |
| **`MapToDomain`** | Method/function MapToDomain in LrclibLyricsProvider.cs. | `function`, `method` |
| **`GetCachedLyricsAsync`** | Method/function GetCachedLyricsAsync in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`IsMarkedNotFoundAsync`** | Method/function IsMarkedNotFoundAsync in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`SaveLyricsAsync`** | Method/function SaveLyricsAsync in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`MarkNotFoundAsync`** | Method/function MarkNotFoundAsync in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`GetTrackOffsetAsync`** | Method/function GetTrackOffsetAsync in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`SetTrackOffsetAsync`** | Method/function SetTrackOffsetAsync in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`GenerateRawLrc`** | Method/function GenerateRawLrc in SqliteLyricsCacheRepository.cs. | `function`, `method` |
| **`BuildTargetModel`** | Method/function BuildTargetModel in 20260823130609_InitialCreate.Designer.cs. | `function`, `method` |
| **`Up`** | Method/function Up in 20260823130609_InitialCreate.cs. | `function`, `method` |
| **`Down`** | Method/function Down in 20260823130609_InitialCreate.cs. | `function`, `method` |
| **`BuildModel`** | Method/function BuildModel in CantusDbContextModelSnapshot.cs. | `function`, `method` |
| **`OnModelCreating`** | Method/function OnModelCreating in CantusDbContext.cs. | `function`, `method` |
| **`SpotifyAuthService`** | Method/function SpotifyAuthService in SpotifyAuthService.cs. | `function`, `method` |
| **`GetAuthorizationUri`** | Method/function GetAuthorizationUri in SpotifyAuthService.cs. | `function`, `method` |
| **`ExchangeCodeAsync`** | Method/function ExchangeCodeAsync in SpotifyAuthService.cs. | `function`, `method` |
| **`RefreshTokenAsync`** | Method/function RefreshTokenAsync in SpotifyAuthService.cs. | `function`, `method` |
| **`GetSessionAsync`** | Method/function GetSessionAsync in SpotifyAuthService.cs. | `function`, `method` |
| **`GetAllSessionsAsync`** | Method/function GetAllSessionsAsync in SpotifyAuthService.cs. | `function`, `method` |
| **`RevokeSessionAsync`** | Method/function RevokeSessionAsync in SpotifyAuthService.cs. | `function`, `method` |
| **`GetCurrentPlaybackAsync`** | Method/function GetCurrentPlaybackAsync in SpotifyPlayerClient.cs. | `function`, `method` |
