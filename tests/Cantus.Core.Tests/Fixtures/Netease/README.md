# NetEase lyric fixtures

Responses from `GET https://music.163.com/api/song/lyric?id={id}&lv=1&yv=1&tv=-1`, captured during the spike for issue #65.

Each file keeps the real JSON shape, timestamps and token layout, trimmed to a few lines. Every lyric word, credit name, nickname and description is replaced with a placeholder:

- Latin words become NATO words (`alpha`, `bravo`, ... then `alpha2`, ...).
- CJK characters become heavenly-stem characters (`甲乙丙丁...`).
- Credit labels (`作词`, `作曲`, `编曲`, ...) are kept. Unknown labels are replaced like lyrics.

Placeholders are assigned in order across the whole file, so the same word in `lrc` and `yrc` gets different placeholders. Do not assert that text matches across keys.

| File | Shows |
| :--- | :--- |
| `word-timed-latin.json` | `yrc` word timing for an English track: credit header lines, blank separator line, syllable tokens without a space, zero and short punctuation tokens |
| `word-timed-cjk.json` | `yrc` with one token per CJK character, plus `ytlrc` and `yromalrc` |
| `word-timed-credit-block.json` | Credit lines at `[0,0]`, then credit lines timed per character with a full-width colon, full-width parentheses as tokens, a line starting at 1 ms, then two lyric lines |
| `word-timed-stretched-credit.json` | A credit line timed as if sung, spanning 52 seconds before the first lyric |
| `line-only.json` | `lrc` and `tlyric` only, no `yrc` key |
| `pure-music.json` | Instrumental: `pureMusic: true` and an empty `lrc`, no `nolyric` flag |
| `uncollected.json` | `uncollected: true` and an empty `lrc` |
