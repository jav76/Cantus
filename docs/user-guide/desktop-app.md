# Desktop App: Choose Your Server

The desktop app (Linux, Windows, macOS) shows lyrics from a Cantus server, so it needs that server's address. The web app never asks: it uses the server it was opened from.

---

## First Launch

The first time the app starts, it asks **Which Cantus server do you use?**

1. Enter the address you open Cantus at in a browser, such as `http://192.168.0.10:5000` or `https://lyrics.example.com`.
2. Select **Connect**. The app checks that a Cantus server answers there, then opens.

Select **Skip** if the server runs on this computer. The app then uses `http://127.0.0.1:5000`. Either choice is remembered, so the question is not asked again.

| Message | Meaning | What to do |
| :--- | :--- | :--- |
| Enter a full address that starts with http:// or https:// | The address is missing `http://` or `https://`. | Add it. `192.168.0.10:5000` alone is not enough. |
| No Cantus server answered at ... | Nothing replied at that address within 5 seconds. | Check the address, and that the server is running and reachable from this computer. To keep the address anyway, select **Save anyway**. |

!!! note "Spotify sign-in needs HTTPS on a remote server"
    Spotify only accepts `http://` on `127.0.0.1`. A plain `http://192.168.0.10:5000` server connects, but Connect Spotify fails through it. See [Reverse Proxy](../operator-guide/reverse-proxy.md).

---

## Change the Server Later

1. Open the settings: the track card on wide windows, or the **Settings** tab on narrow ones.
2. Under **Server**, select **Change**.
3. Enter the new address and select **Connect**. **Use the server on this computer** fills in `http://127.0.0.1:5000`.

When the address changes, Cantus restarts to connect to the new server. If it cannot restart itself, it asks you to close and reopen it.

!!! warning "Changing the server signs this device out"
    A sign-in and a Display pairing belong to one server. After a change, sign in again, or pair the screen again if it was a [Display](displays.md). On the old server the Display stays in its owner's list until they remove it.

---

## Started With an Address

An address given at start-up wins over the saved one, and the app does not ask on first launch:

- `--server-url <address>` (or `-s`) on the command line.
- The `CANTUS_SERVER_URL` environment variable.

While one of these is set, **Server** shows that address and it cannot be changed in the app. See [Configuration Reference](../reference/configuration.md#cli-option-server-url-alias-s-desktop-client-only).

---

## Where It Is Stored

The address is saved with the app's other per-device settings, in the app data folder of your OS user (`~/.local/share/Cantus.Client/Settings` on Linux). Deleting that folder brings back the first-launch question.
