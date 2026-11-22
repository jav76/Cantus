# Glossary

**Session**: The credential a person gets by signing in with Spotify. It identifies them as the account owner and allows playback control and account management.
_Avoid_: login, user id, account token

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
