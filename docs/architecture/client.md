# Client Presentation Layer (Uno Platform)

The client presentation layer provides cross-platform UI views built on Uno Platform, supporting WebAssembly (browser), Linux (Skia/X11), and Windows (Desktop). It implements the MVVM pattern with real-time SignalR subscriptions, dynamic lyric scrolling, and cover art palette blending.

## Layer Metadata

- **Layer ID**: `layer:client-ui`
- **Component Count**: `48`
- **Role**: Cross-platform UI views, ViewModels, dynamic theming, and real-time SignalR playback client.

## Key Components & Files

| Component | Type | Summary | Complexity |
| :--- | :---: | :--- | :---: |
| **`App.xaml`** | `file` | XAML UI view declaration for App. | `moderate` |
| **`App.xaml.cs`** | `file` | Code-behind companion class for App. | `moderate` |
| **`App`** | `class` | Class App providing core functionality in App.xaml.cs. | `moderate` |
| **`GlobalUsings.cs`** | `file` | Source file: GlobalUsings.cs. | `simple` |
| **`MainPage.xaml`** | `file` | XAML UI view declaration for MainPage. | `moderate` |
| **`MainPage.xaml.cs`** | `file` | Code-behind companion class for MainPage. | `moderate` |
| **`MainPage`** | `class` | Class MainPage providing core functionality in MainPage.xaml.cs. | `complex` |
| **`AppTheme.cs`** | `file` | Data model / entity definition: AppTheme. | `simple` |
| **`ColorPalette`** | `class` | Class ColorPalette providing core functionality in AppTheme.cs. | `complex` |
| **`ColorExtractionHelper.cs`** | `file` | Helper generating dynamic complementary and background palettes from album art image metadata. | `moderate` |
| **`ColorExtractionHelper`** | `class` | Class ColorExtractionHelper providing core functionality in ColorExtractionHelper.cs. | `moderate` |
| **`Program.cs`** | `file` | Application bootstrap entry point for Desktop. | `simple` |
| **`Program`** | `class` | Class Program providing core functionality in Program.cs. | `moderate` |
| **`Program.cs`** | `file` | Application bootstrap entry point for WebAssembly. | `simple` |
| **`SignalRPlaybackClient.cs`** | `file` | Client-side SignalR connection manager handling automatic reconnect, NTP ping/pong synchronization, and event dispatch. | `complex` |
| **`SignalRPlaybackClient`** | `class` | Class SignalRPlaybackClient providing core functionality in SignalRPlaybackClient.cs. | `complex` |
| **`ThemeManager.cs`** | `file` | Client dynamic theme manager supporting predefined dark/light color schemes and cover art palette generation. | `moderate` |
| **`ThemeManager`** | `class` | Class ThemeManager providing core functionality in ThemeManager.cs. | `complex` |
| **`LyricLineViewModel.cs`** | `file` | Data model / entity definition: LyricLineViewModel. | `simple` |
| **`LyricLineViewModel`** | `class` | Class LyricLineViewModel providing core functionality in LyricLineViewModel.cs. | `complex` |
| **`LyricsViewModel.cs`** | `file` | Uno Platform MVVM ViewModel orchestrating real-time lyrics scrolling, theme dynamic switching, and clock synchronization. | `complex` |
| **`LyricsViewModel`** | `class` | Class LyricsViewModel providing core functionality in LyricsViewModel.cs. | `complex` |
| **`DiagnosticsHudDialog.xaml`** | `file` | XAML UI view declaration for DiagnosticsHudDialog. | `moderate` |
| **`DiagnosticsHudDialog.xaml.cs`** | `file` | Code-behind companion class for DiagnosticsHudDialog. | `moderate` |
| **`DiagnosticsHudDialog`** | `class` | Class DiagnosticsHudDialog providing core functionality in DiagnosticsHudDialog.xaml.cs. | `moderate` |
| **`index.html`** | `file` | Source file: index.html. | `simple` |

## Member Functions & Endpoints

| Symbol | Summary | Tags |
| :--- | :--- | :--- |
| **`OnLaunched`** | Method/function OnLaunched in App.xaml.cs. | `function`, `method` |
| **`InitializeLogging`** | Method/function InitializeLogging in App.xaml.cs. | `function`, `method` |
| **`MainPage`** | Method/function MainPage in MainPage.xaml.cs. | `function`, `method` |
| **`OnActiveLineChanged`** | Method/function OnActiveLineChanged in MainPage.xaml.cs. | `function`, `method` |
| **`OnPageKeyDown`** | Method/function OnPageKeyDown in MainPage.xaml.cs. | `function`, `method` |
| **`GeneratePaletteFromMetadata`** | Method/function GeneratePaletteFromMetadata in ColorExtractionHelper.cs. | `function`, `method` |
| **`HslToRgb`** | Method/function HslToRgb in ColorExtractionHelper.cs. | `function`, `method` |
| **`Main`** | Method/function Main in Program.cs. | `function`, `method` |
| **`Main`** | Method/function Main in Program.cs. | `function`, `method` |
| **`StartAsync`** | Method/function StartAsync in SignalRPlaybackClient.cs. | `function`, `method` |
| **`SyncClockAsync`** | Method/function SyncClockAsync in SignalRPlaybackClient.cs. | `function`, `method` |
| **`ProcessNtpSample`** | Method/function ProcessNtpSample in SignalRPlaybackClient.cs. | `function`, `method` |
| **`UpdateTrackMetadata`** | Method/function UpdateTrackMetadata in ThemeManager.cs. | `function`, `method` |
| **`ApplyTheme`** | Method/function ApplyTheme in ThemeManager.cs. | `function`, `method` |
| **`UpdateVisualProperties`** | Method/function UpdateVisualProperties in LyricLineViewModel.cs. | `function`, `method` |
| **`LyricsViewModel`** | Method/function LyricsViewModel in LyricsViewModel.cs. | `function`, `method` |
| **`OnPlaybackStateReceived`** | Method/function OnPlaybackStateReceived in LyricsViewModel.cs. | `function`, `method` |
| **`OnLyricsReceived`** | Method/function OnLyricsReceived in LyricsViewModel.cs. | `function`, `method` |
| **`OnDiagnosticsReceived`** | Method/function OnDiagnosticsReceived in LyricsViewModel.cs. | `function`, `method` |
| **`OnTick`** | Method/function OnTick in LyricsViewModel.cs. | `function`, `method` |
| **`EvaluateInstrumentalBreak`** | Method/function EvaluateInstrumentalBreak in LyricsViewModel.cs. | `function`, `method` |
| **`FindActiveLineIndex`** | Method/function FindActiveLineIndex in LyricsViewModel.cs. | `function`, `method` |
