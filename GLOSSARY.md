# Glossary

**Session**: The credential a person gets by signing in with Spotify. It identifies them as the account owner and allows playback control and account management.
_Avoid_: login, user id, account token

**Display**: A screen paired to an owner's Session that shows their lyrics and playback. It holds its own view-only credential, and the owner can list and revoke each Display separately.
_Avoid_: room, kiosk session, shared session

**Display token**: The credential a Display holds. It grants viewing of one owner's playback and nothing else.
_Avoid_: room id, access token (that term means the Spotify token)
