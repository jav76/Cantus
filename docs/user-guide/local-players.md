# Use This Computer's Players

The desktop app can show lyrics for whatever a media player on your computer is playing, with no Spotify account. Cantus reads the player directly and sends its track and position to the server, which finds the lyrics as usual. Your [Displays](displays.md) follow along too.

This is a **Local Session**. It works with any player that reports its track: the Spotify app, a browser tab, or a music app.

!!! note "Linux desktop app only, for now"
    Cantus reads players through MPRIS, so this needs the desktop app on Linux. The web app and the Windows and macOS desktop apps only offer **Connect Spotify**. The option also only appears when the server's owner has [turned Local Sessions on](../operator-guide/self-hosting.md#6-let-people-use-their-own-players).

---

## Choose It

When you are signed out, Cantus offers **Use this computer's players** next to **Connect Spotify**: in the header, on the track card, and on the **Sync & Info** tab on small screens. The **Connect Spotify** setup screen also has **Use this computer's players instead**.

Select it, and Cantus creates a Local Session named after this computer. If the server asks for a **join code**, Cantus asks you for it first; get the code from the server's owner. A wrong code keeps the box open so you can try again.

Once signed in, the account button in the header reads as this computer, not Spotify. Select it to see:

- **This computer**: the name the server knows this computer by.
- **Following**: the player Cantus shows lyrics for, or **No player playing**.
- **Connect Spotify instead**: signs out of the Local Session and starts the Spotify sign-in.
- **Sign out**.

---

## Which Player Cantus Follows

Cantus follows one player at a time: the one that most recently **started playing**. A player that is paused stays followed until another one starts.

A track only counts when the player reports a title and an artist, and it lasts from **30 seconds to 20 minutes**. So a notification sound, a short clip in a browser or a long video does not take the lyrics away from your music.

### Turn Players Off

**Players on this computer**, on the track card (the **Sync & Info** tab on small screens), lists each player Cantus has seen, with a switch for each and a marker on the one it follows. Switch a player off and Cantus stops following it at once. The switches are saved on this computer.

`playerctld` and KDE Connect start switched off, because they mirror other players (KDE Connect shows your phone's).

---

## Sign Out or Switch to Spotify

**Sign out** ends the Local Session on the server and removes its Displays. Cantus goes back to the choice between **Connect Spotify** and **Use this computer's players**; nothing signs you back in by itself.

To switch, use **Connect Spotify instead** from the account menu. A Spotify Session's account menu has **Use this computer's players instead** for the other direction.

If you close the desktop app, your Displays show the track paused after about 20 seconds, and pick up again when the app reopens.

---

## What Does Not Work Yet

- **Playback controls from Cantus**: play, pause, skip and seek do not reach the player yet. Use the player itself. Volume, shuffle and repeat are hidden.
- **Find a Song by Lyrics** is hidden: it searches and plays through Spotify.
- **Cover art** from some players is missing, when the player only offers a file on this computer.

Timing offsets work as usual and are saved per track.

---

## Privacy

The server's owner can see this computer's name (the Session name) and the title, artist and album of what you play, as with Spotify playback. As always, title, artist, album and duration go to the lyrics providers to look up lyrics.
