# Glossary

**Session**: The credential that identifies a person as the owner of their playback and Displays; either a Spotify Session (from signing in with Spotify) or a Local Session. It allows account management, and playback control for a Spotify Session.
_Avoid_: login, user id, account token

**Local Session**: A Session with no Spotify account, created by the desktop app when the owner chooses this computer's players. Its playback comes from the Publisher, not from Spotify.
_Avoid_: guest session, offline session, anonymous session

**Local player**: A media player on the owner's computer that the desktop app can read, such as Spotify, a browser or a music app (through MPRIS on Linux).
_Avoid_: device (that term means a Spotify Connect device), source, app

**Followed player**: The one Local player whose track a Local Session shows: the enabled player that most recently started playing a track with a title, an artist and a length of 30 seconds to 20 minutes.
_Avoid_: active player, current device, selected player

**Publisher**: The desktop app connection that sends a Local Session's playback to the server. A Session has at most one; the newest connection to publish takes over.
_Avoid_: poller (that term means the server's Spotify polling), host, broadcaster

**Playback source**: Where a playback state came from: `Spotify` (the server polled the Spotify Web API) or `Local` (a Publisher sent it).
_Avoid_: provider (that term means a lyrics provider), origin, backend

**Join code**: An optional secret the server's operator sets that a person must enter to create a Local Session. It is not a pairing code, which approves a Display.
_Avoid_: pairing code, invite code, password

**Track key**: The id the server stores lyrics, overrides and offsets under. It is the Spotify track id for Spotify playback, and a key derived from artist, title and duration (starting `local:`) for Local playback.
_Avoid_: track id (for Local playback), hash, fingerprint

**Display**: A screen paired to an owner's Session that shows their lyrics and playback. It holds its own view-only credential, and the owner can list and revoke each Display separately.
_Avoid_: room, kiosk session, shared session

**Display token**: The credential a Display holds. It grants viewing of one owner's playback and nothing else.
_Avoid_: room id, access token (that term means the Spotify token)

**Fragment**: The remembered lyric text an owner types to find a song. It may be inexact and may span two lyric lines.
_Avoid_: query, search term, snippet

**Guess**: One ranked song a lookup returns for a Fragment, from the server's library or an online lyrics provider, with a match score. Versions of the same song are grouped into one Guess.
_Avoid_: result, match, hit, candidate (that term means a lyrics override candidate)

**Own Spotify app**: A Spotify Developer app a user creates and signs in with by its Client ID, instead of the server's app. It has its own 5-user limit.
_Avoid_: API key, user key, BYO key
