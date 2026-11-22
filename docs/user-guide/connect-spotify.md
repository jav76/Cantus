# Connect Spotify

Cantus reads your playback through Spotify's sign-in. Select **Connect Spotify** in the header (on a small screen, in Settings).

---

## Which Spotify App?

Spotify only lets an app in Development Mode serve **5 people**, all added by the app's owner. So when you connect, Cantus may offer two choices:

- **Use this server's Spotify app**: works if the server's owner added your Spotify account to their app.
- **Use my own Spotify app**: works for anyone with Spotify Premium. You create the app once, in about two minutes.

If the server has no Spotify app of its own, Cantus goes straight to the own-app steps. If the server only allows its own app, Connect Spotify opens Spotify's sign-in directly.

---

## Create Your Own Spotify App

1. Choose **Use my own Spotify app**, then **Open the Spotify Developer Dashboard**. Sign in with a **Premium** account and select **Create app**. Any name and description will do.
2. Under **Redirect URIs**, paste the address Cantus shows (use its **Copy** button) and select **Add**. Under **Which APIs are you planning to use?**, tick **Web API**. Accept the terms and **Save**.
3. Open the app's **Settings**, copy the **Client ID** (32 letters and digits) and paste it into Cantus. You do not need the Client Secret.
4. Select **Connect** and approve the Spotify consent screen.

Cantus remembers the Client ID on this device, so the next sign-in only needs **Connect**. Select **Forget the saved Client ID** to remove it.

To let family members use your app too, add up to 4 more people by name and Spotify email under **User Management** in the app's settings.

!!! warning "The redirect URI warning"
    Spotify only accepts `https://` addresses, or `http://` on `127.0.0.1`. If Cantus warns that Spotify will refuse the address, open Cantus at `http://127.0.0.1:5000` on the server's own machine, or ask the server's owner to set up [HTTPS](../operator-guide/reverse-proxy.md).

---

## When Sign-in Fails

Cantus shows a page that names the problem. The common ones:

| Page title | What to do |
| :--- | :--- |
| Your account is not on this app's user list | Ask the app's owner to add you under User Management, or select **Set up your own Spotify app**. |
| The Spotify app owner needs Premium | Create your app with a Premium account. |
| Spotify did not accept this Client ID | Copy the Client ID again from your app's Settings page. |
| Spotify rejected the sign-in | Check that your app's Redirect URIs include the address on the page exactly. |
| Sign-in expired | Start again from Cantus, in the same browser. |

The full list is in [Troubleshooting](../operator-guide/troubleshooting.md#sign-in-failure-pages).
