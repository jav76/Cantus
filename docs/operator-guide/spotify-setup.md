# Spotify Developer Setup

Cantus utilizes Spotify's OAuth 2.0 PKCE (Proof Key for Code Exchange) authorization flow. This allows Cantus to securely inspect playback state without requiring a client secret.

---

## 1. Create a Spotify Developer Application

1. Visit the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and log in with your Spotify account.
2. Click **Create app**.
3. Fill out the application details:
   - **App name**: `Cantus Lyrics`
   - **App description**: `Self-hosted synchronized lyrics display`
   - **Redirect URI**: `http://localhost:5000/api/auth/callback` (or `https://lyrics.yourdomain.com/api/auth/callback` for remote hosting)
   - **APIs used**: Select **Web API**.
4. Accept the Spotify Developer Terms of Service and click **Save**.

---

## 2. Obtain Client ID

1. In your Spotify Developer App dashboard, locate **Client ID**.
2. Copy this value and paste it into your `.env` file or Docker Compose configuration as `SPOTIFY_CLIENT_ID`.

> [!NOTE]
> Because Cantus uses the **PKCE** flow, you **do not** need to provide or expose your Spotify Client Secret.

---

## 3. Configure Redirect URIs

If you access Cantus from multiple addresses (e.g. `localhost` and a local LAN IP like `http://192.168.1.100:5000`), add all corresponding callback URLs under **Redirect URIs** in your Spotify App settings:

- `http://localhost:5000/api/auth/callback`
- `http://127.0.0.1:5000/api/auth/callback`
- `https://lyrics.yourdomain.com/api/auth/callback`
