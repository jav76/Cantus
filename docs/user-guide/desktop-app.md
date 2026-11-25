# Desktop App: Choose Your Server

The desktop app (Linux, Windows, macOS) shows lyrics from a Cantus server, so it needs that server's address. The web app never asks: it uses the server it was opened from.

---

## First Launch

The first time the app starts, it asks **Which Cantus server do you use?**

1. Enter the address you open Cantus at in a browser, such as `http://192.168.0.10:5000` or `https://lyrics.example.com`.
2. Select **Connect**. The app checks that a Cantus server answers there, then opens.

No server yet? Select **Run it on this computer**. The app starts the Cantus server that ships with it and uses `http://127.0.0.1:5000`. See [The Server on This Computer](#the-server-on-this-computer).

The Linux AppImage ships no server. Its button reads **Skip**, and uses a server you run yourself at `http://127.0.0.1:5000`.

Either choice is remembered, so the question is not asked again.

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

## The Server on This Computer

The Windows installer and the Windows, macOS and Linux archives include a Cantus server. The app runs it for you when its server address is `http://127.0.0.1:5000`.

- **It starts with the app.** If a Cantus server already answers at that address, the app uses it and starts nothing.
- **It keeps running after you close the app**, so the next start is quick.
- **It only listens on this computer.** Other devices on your network cannot reach it. To use phones or TVs as Displays, [self-host the server](../operator-guide/self-hosting.md) instead.
- **This computer's players are allowed** with no join code, since only this computer can reach the server. See [Use This Computer's Players](local-players.md).

**Stop it:** open **Server** in the settings, select **Change**, then **Stop the server on this computer**. It starts again the next time Cantus opens. A server you started yourself is never stopped by the app.

**After an update:** the app replaces a server it started from an older version. The Windows installer stops the server before it updates the files.

| Symptom | Cause | What to do |
| :--- | :--- | :--- |
| The app reports that no Cantus server answered at `http://127.0.0.1:5000` | Another program uses port 5000 (on macOS, often AirPlay Receiver). | Free the port, or run the server yourself on another port and enter that address. |
| **Stop the server on this computer** says the app did not start it | The server was started outside the app. | Stop it where you started it. |

Its data is in your user profile, separate from the app's settings:

| OS | Folder |
| :--- | :--- |
| Windows | `%LOCALAPPDATA%\Cantus\server` |
| Linux | `~/.local/share/Cantus/server` |
| macOS | `~/Library/Application Support/Cantus/server` |

Deleting that folder while the server is stopped removes its sign-ins, Displays and lyrics cache.

---

## Started With an Address

An address given at start-up wins over the saved one, and the app does not ask on first launch:

- `--server-url <address>` (or `-s`) on the command line.
- The `CANTUS_SERVER_URL` environment variable.

While one of these is set, **Server** shows that address and it cannot be changed in the app. See [Configuration Reference](../reference/configuration.md#cli-option-server-url-alias-s-desktop-client-only).

---

## Where It Is Stored

The address is saved with the app's other per-device settings, in the app data folder of your OS user (`~/.local/share/Cantus.Client/Settings` on Linux). Deleting that folder brings back the first-launch question.
