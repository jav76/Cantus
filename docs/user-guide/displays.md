# Pairing & Managing Displays

A **Display** is a screen, such as a TV or a wall tablet, that shows your lyrics and playback without signing in to Spotify on it. You pair it once from a phone or computer that is signed in to Cantus. The screen then holds its own view-only Display token, so a shared screen never gets your account's full rights.

Use a Display for any screen other people can reach. Sign in with Spotify only on devices you want to control playback from.

---

## What a Display Can and Cannot Do

| A Display can | A Display cannot |
| :--- | :--- |
| Show your current track, artwork, progress and synced lyrics | Play, pause, skip, seek, or change volume, shuffle or repeat |
| Follow your playback live, with the same timing as your own devices | Change per-track sync offsets or pick different lyrics |
| Adjust its own **Device latency** (and use <kbd>[</kbd> and <kbd>]</kbd>) to correct the screen's lag | Sign in, sign out, or see your sign-in |
| Keep your playback updating while no other device is open | List, approve, rename or remove Displays |
| Reconnect by itself after a restart or network drop | Watch anyone else's playback |

A Display hides the transport controls, seeking, sync offset steppers, the **Wrong lyrics?** picker and account actions. In their place it shows **Paired display (view only)** with its name.

---

## Pair a Screen

You need two devices: the screen you want to pair, and a phone or computer signed in to Cantus, with Spotify or with [this computer's players](local-players.md).

### 1. Show a code on the screen

1. On the screen, open your Cantus address, for example `http://<your-server-ip>:5000`.
2. In the **Account** section, choose **Pair as display**. On mobile layouts it is under the **Sync & Info** tab. The button only shows while the screen is signed out, so sign out there first if it is signed in.
3. The screen shows a short code in large type, a QR code, the server address and how long the code is valid.

Open the screen at the same address your phone uses (for example your public HTTPS address). The QR code and the address under the code are built from it.

### 2. Approve the code

Approve it from your signed-in phone or computer in one of two ways.

=== "Scan the QR code"

    1. Scan the QR code with your phone's camera. It opens `<your Cantus address>/?pair=<CODE>`.
    2. Cantus opens the **Approve a display** prompt with the code filled in.
    3. Give the screen a name of 1 to 64 characters, such as "Living room TV", and choose **Approve**.

=== "Type the code"

    1. In the **Displays** section, choose **Add display**. It is under the **Account** section, and under the **Sync & Info** tab on mobile layouts.
    2. Type the code shown on the screen. Dashes and letter case do not matter.
    3. Give the screen a name of 1 to 64 characters and choose **Approve**.

The screen switches to the lyrics view by itself. It stores its token on the device (browser storage in the web app, local settings in the desktop app), so it comes back as the same Display after a restart.

### If the phone is not signed in

If the QR opens Cantus in a browser that is not signed in, the prompt asks you to sign in first. Choose **Connect Spotify** in the prompt (or **Use this computer's players**, where the desktop app offers it). Cantus keeps the code on this device, and the prompt opens again with it once you are back and signed in. A kept code is dropped if you cancel the prompt, or after 5 minutes, when the screen's code has expired anyway.

### About pairing codes

- A code works once and expires after 5 minutes by default.
- The screen gets a new code by itself when its code expires or its connection to the server drops, so a waiting screen always shows a code that works.
- Choose **Cancel** on the screen to stop pairing and go back to the normal view.

---

## Manage Your Displays

The **Displays** section lists every screen paired to your account, oldest first, with when each one last connected. The list updates by itself when a Display is paired, renamed or removed, from this device or another one, and when a Display connects. The last-seen times age as you watch, and the refresh button fetches the list again.

### Rename a Display

1. Choose the pencil button next to the Display.
2. Enter a new name of 1 to 64 characters and choose **Save**.

The new name shows on every device you are signed in on.

### Remove (revoke) a Display

1. Choose the trash button next to the Display.
2. Confirm with **Remove**.

Removing a Display takes effect at once:

- The screen is disconnected, discards its token, and goes back to the pairing screen with the message "This display was removed by its owner. Pair it again to keep showing lyrics."
- Your own sign-in and your other Displays are not affected.
- To use that screen again, pair it again. It gets a new token.

Remove a Display you no longer use, or one you think someone else has copied. A Display token does not expire, so removing it is the only way to stop it.

### Logging out removes your Displays

Displays belong to your sign-in on this Cantus server, whether it is a Spotify sign-in or a Local Session. Signing out ends that sign-in on every device and removes every Display paired to it. Each screen goes back to the pairing screen the next time it reconnects. Pair them again after you sign back in.

---

## When a Screen Shows a Message

| Message on the screen | What it means | What to do |
| :--- | :--- | :--- |
| This display was removed by its owner. Pair it again to keep showing lyrics. | The Display was removed from the **Displays** section. | Pair it again if you still want it. |
| This display's pairing is no longer valid. Pair it again to keep showing lyrics. | The server refused the token several times in a row, for example when the screen was offline while you logged out. A short server restart alone does not cause this: the screen retries first. | Pair it again. |
| Too many pairing requests. Trying again shortly. | Too many codes were requested from your network in a short time. | Wait. The screen retries by itself. |
| Too many attempts. Wait a minute and try again. | Shown to the owner: too many approvals were tried from your network in a short time. | Wait a minute, then approve again. |
| The server has too many pending pairings. Trying again shortly. | Many screens are waiting for approval at once. | Wait, or approve or cancel the other waiting screens. |
| Could not get a pairing code. Trying again shortly. | The screen could not reach the server. | Check the network. The screen retries by itself. |

On the approving device, "That code is not valid or has expired" means the code was mistyped, already used, or older than 5 minutes, or the screen disconnected. Check the code that the screen shows now.

---

## Desktop App as a Display

The desktop app can be a Display too. The QR code and the address under the code use the server address the app connects to. Set the address a phone can reach, such as `http://<your-server-ip>:5000`, under **Server** in the app's settings (see [Desktop App: Choose Your Server](desktop-app.md)), or start the app with `--server-url` or `CANTUS_SERVER_URL`. The default `127.0.0.1` only works on the same machine.

For server-side details, such as what a Display token grants and how pairing behaves behind a reverse proxy, see [Displays & Display Tokens](../operator-guide/displays.md).
