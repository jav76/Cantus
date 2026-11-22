# Displays & Display Tokens

A **Display** is a screen paired to an owner's Session that shows their lyrics and playback. It holds a view-only **Display token** instead of a Session. This page covers what that token grants on your server, how long it lasts and how it is revoked. For the pairing steps users follow, see [Pairing & Managing Displays](../user-guide/displays.md).

---

## What a Display Token Grants

A Display token lets one screen watch one owner's playback. Everything else is closed to it.

| A Display token can | A Display token cannot |
| :--- | :--- |
| Open a [PlaybackHub](../reference/signalr-api.md#credential-kinds) connection bound to its owner | Control playback: player commands, volume, seek, shuffle and repeat are rejected |
| Receive the owner's playback state, lyrics, track offsets and diagnostics | Change track offsets or lyrics overrides, or list lyric candidates |
| Run clock sync, report visibility and ask for a refresh (throttled to one per owner every 5 seconds) | Call any owner-only endpoint: each returns `403 Forbidden` and changes nothing |
| Keep the owner's adaptive polling running while it is connected | List, approve, rename or revoke Displays |
| | Read the owner's Session id, Spotify tokens or session list |
| | Join the sign-in handoff that would hand it the owner's Session |
| | See any other owner's playback |

Payloads sent to a Display have the owner's Session id removed, because that id is itself a credential. A Display still sees what the owner's screen would show: the track, progress, the Spotify device name and volume, and the owner's Spotify display name.

The full list of rejected endpoints and hub methods is in the [REST API reference](../reference/rest-api.md#display-token) and the [SignalR reference](../reference/signalr-api.md#credential-kinds).

!!! note "Spotify quota"
    A connected Display counts as a viewer of its owner, so an unattended TV keeps polling Spotify for that owner. Its forced refreshes are throttled, so a Display cannot push the owner's quota toward a `429`. See [Adaptive Polling](../architecture/adaptive-polling.md).

---

## Token Storage

- A Display token is 256 random bits, generated when the owner approves a code. It is sent once, to the waiting screen's hub connection, and never appears in an HTTP response.
- The server stores only a SHA-256 hash of the token in the `Displays` table of the SQLite database. A copy of the database or a backup does not contain working Display tokens. It does hold each owner's Session id in plain text, which is a full owner credential (it can approve pairing codes and mint new Display tokens), so keep backups as private as the live database.
- The token does not use the Data Protection key ring. Rotating or losing `/app/data/keys/` does not affect Displays.
- The screen keeps the token in browser storage (web app) or local settings (desktop app). Anyone who can read that device's storage can copy the token and watch the owner's playback from anywhere that can reach your server, until the Display is revoked.
- Several connections may use the same token at once. Revoking the Display closes all of them.

---

## Lifetime

**Display tokens do not expire.** A token keeps working until one of these happens:

- The owner removes the Display (`DELETE /api/displays/{displayId}`, the trash button in the client).
- The owner logs out, or their Session is revoked. The Session row is deleted and its Displays are deleted with it.
- The database is reset or replaced.

There is no server-wide setting that expires idle Displays. Each Display's last-seen time is recorded (at most once a minute) and shown to the owner, so stale screens are easy to spot and remove.

---

## How Revocation Works

When the owner removes a Display:

1. Its database row is deleted, so the token is refused on the next request or connection.
2. Each of its open hub connections receives `ReceiveDisplayRevoked` and is then closed. A connection whose token was checked just before the revocation is closed as soon as it finishes connecting.
3. The owner's Session connections receive `ReceiveDisplaysChanged` and refresh their Displays list.

The owner's Session, its connections and the owner's other Displays are not touched. The client discards the token and shows its pairing screen again.

When the owner logs out, or their Session is revoked, every Display of that Session is deleted the same way: each open Display connection gets `ReceiveDisplayRevoked` and is closed at once, and the client returns to its pairing screen.

---

## Pairing on Your Server

- Pending pairing codes live in server memory. A restart drops them, and waiting screens request new codes by themselves.
- Pending codes and Display connection tracking are kept per process. Run a single Cantus instance; replicas behind a load balancer would not see each other's codes or close each other's revoked connections.
- Pairing requests and approvals are rate limited per client address (10 each per 60 seconds by default, with IPv6 addresses in one `/64` counted as one client). Extra requests get `429 Too Many Requests`. See [Display pairing behind a reverse proxy](reverse-proxy.md#display-pairing-behind-a-reverse-proxy) for how the client address is found.
- Code length, code lifetime, the pending limit and the rate limit are set under [`DisplayPairing`](../reference/configuration.md#display-pairing-displaypairing).

The server logs `Display {DisplayId} created`, `Display {DisplayId} paired to connection {ConnectionId}` and `Display {DisplayId} revoked` at `Information`. A Display that calls an owner-only hub method is logged at `Warning` as `Rejected {Action} from Display connection {ConnectionId}`.
