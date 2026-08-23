# Configuration Reference

Complete reference of configuration options, environment variables, and `appsettings.json` schemas for Cantus.

---

## Environment Variables

| Variable | Type | Default | Description |
| :--- | :---: | :---: | :--- |
| `SPOTIFY_CLIENT_ID` | `string` | *(Required)* | Spotify Developer Application Client ID. |
| `CANTUS_HOST_URL` | `string` | `http://localhost:5000` | Publicly accessible URL of the Cantus server used for OAuth callback resolution. |
| `ASPNETCORE_ENVIRONMENT` | `string` | `Production` | ASP.NET Core environment mode (`Development`, `Staging`, `Production`). |
| `ConnectionStrings__DefaultConnection` | `string` | `Data Source=/app/data/cantus.db` | SQLite connection string. |
| `PlaybackPoller__ActiveIntervalMs` | `int` | `500` | Spotify polling cadence during active playback (milliseconds). |
| `PlaybackPoller__PausedIntervalMs` | `int` | `3000` | Spotify polling cadence when playback is paused (milliseconds). |
| `PlaybackPoller__IdleIntervalMs` | `int` | `10000` | Spotify polling cadence when no playback is active (milliseconds). |
| `LyricsCache__CacheDurationDays` | `int` | `30` | Expiration lifetime for cached positive lyric entries (days). |
| `LyricsCache__NegativeCacheDurationDays` | `int` | `7` | Expiration lifetime for tracks with no available lyrics (days). |

---

## `appsettings.json` Schema

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/app/data/cantus.db"
  },
  "Spotify": {
    "ClientId": ""
  },
  "Cantus": {
    "HostUrl": "http://localhost:5000"
  },
  "PlaybackPoller": {
    "ActiveIntervalMs": 500,
    "PausedIntervalMs": 3000,
    "IdleIntervalMs": 10000
  },
  "LyricsCache": {
    "CacheDurationDays": 30,
    "NegativeCacheDurationDays": 7
  }
}
```
