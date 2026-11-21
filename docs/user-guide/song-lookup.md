# Find a Song by Lyrics

Remember a line but not the song? Type the line into Cantus. It searches the lyrics the server already knows, looks online when needed, and lets you play or queue the best guess on Spotify.

---

## How to Use It

1. Sign in with Spotify. Find Song is only offered to a signed-in owner, never on a paired Display.
2. Select **Find Song** in the header. On a small screen it is in the overflow menu as **Find Song by Lyrics**.
3. Type a line you remember and press <kbd>Enter</kbd> or **Search**. It does not have to be exact: a wrong word, a typo, a missing apostrophe or missing accents are fine.
4. Pick a guess and select **Play** or **Queue**.

Tips:

- Type at least 3 words (or 4 Chinese, Japanese or Korean characters). Up to 24 words are used; the box holds 200 characters.
- Distinctive lines work better than a chorus word repeated many times.
- A line that runs across two lyric lines still matches.

---

## Library and Online Results

Every search looks in your **library** first: the lyrics this Cantus server has already fetched for tracks played on it, plus any lyrics corrections. Library guesses show at once and are labelled, for example, "86% match, in your library".

When the library has no clear answer, Cantus searches **online** while the library guesses stay on screen. Online guesses come from NetEase lyric search and LRCLIB, and are labelled "found online (NetEase)" or "found online (LRCLIB)". A guess marked "not verified" is one NetEase returned that Cantus could not match itself (for example the same lyrics in Traditional rather than Simplified Chinese).

If the library answered but it is the wrong song, select **Not it? Search online** to ask the online sources anyway.

The status line tells you how sure Cantus is:

| Status | Meaning |
| :--- | :--- |
| **Best match** | One song clearly matches better than any other. |
| **Several songs could match** | Close guesses, for example two songs that share a chorus line. |
| **Possible matches** | Weak guesses only. Try another line. |
| **No match found** | Nothing scored high enough. |

The percentage is a match score for your line, not a probability that the song is right.

---

## Play and Queue

- **Play** starts the song on your active Spotify device at once. It replaces what was playing, including the album or playlist, so the next track after it is not your previous queue position.
- **Queue** adds the song to the end of your Spotify queue.
- Online guesses are looked up on Spotify when you select Play or Queue. If no Spotify track matches the title and artist closely enough, the card says "Couldn't find this song on Spotify" and nothing plays.
- Spotify needs an active device and a Premium account to play or queue. The card tells you when either is missing.
- Once a song plays, Cantus fetches its lyrics as usual, so next time it can be found in your library.

---

## Limits

- Search requests are limited per network address (20 per minute by default). A search the library cannot answer uses two requests and "Not it? Search online" uses one, so about 10 such searches fit in a minute. Over the limit, the dialog asks you to try again in a minute.
- Online searches are limited per Spotify account (6 per minute by default). Over that limit, library guesses still show and online search is reported as paused.
- When NetEase or LRCLIB is busy or unreachable, you get library guesses and a note that online search failed or is paused.
- Songs in Thai, Lao, Khmer or Burmese (no spaces between words) are not supported.

---

## Privacy

- **Your typed line is sent to NetEase and LRCLIB** when Cantus searches online. The request comes from the Cantus server, not your device, and carries no account details. A search the library answers sends nothing.
- **The library is shared by everyone on this server.** A guess can be a track another Session on the same Cantus server played, which reveals that someone there played it.
- The server's log line for each search records counts, scores and the outcome, not the line you typed or the lyrics.

To keep every search on the server, the operator sets `SongLookup__ExternalSearchEnabled=false` (see [Configuration](../reference/configuration.md#song-lookup-songlookup)). Find Song then searches the library only.
