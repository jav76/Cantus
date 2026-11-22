#!/usr/bin/env python3
"""
scripts/generate_icons.py
Writes the client's icon set (src/Cantus.Client/Cantus.Client/Icons/CantusIcons.g.cs)
from Phosphor Icons, regular weight (MIT, https://github.com/phosphor-icons/core).

Each Phosphor regular SVG is one filled path in a 256 x 256 box. The script
reads that path, rewrites it with one space between every token (arc flags
included) so the client can scale it by multiplying numbers, and emits the
CantusIconKind enum plus a switch from kind to path data. Icons are named by
what they mean in Cantus; several kinds may share one Phosphor icon.

The SVGs are downloaded from a pinned phosphor-icons/core commit and checked
against their SHA-256, or read from --source-dir when they are already on disk.
The Phosphor licence is copied next to the generated file.

Usage: python scripts/generate_icons.py [--source-dir DIR]
"""

import argparse
import hashlib
import re
import tempfile
import urllib.request
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT_DIR / "src" / "Cantus.Client" / "Cantus.Client" / "Icons"
OUTPUT_FILE = OUTPUT_DIR / "CantusIcons.g.cs"
LICENSE_FILE = OUTPUT_DIR / "LICENSE-Phosphor.txt"

PHOSPHOR_COMMIT = "d42782b2abe747d904b971ccab48b182a1455f86"  # v2.0.8
SOURCE_URL = "https://raw.githubusercontent.com/phosphor-icons/core/{commit}/{path}"
LICENSE_SHA256 = "b5b1f1da112d18ea2147decfd48ddc1bf2b5aeb6c265381579340e95b15a2bb2"

PHOSPHOR_SHA256 = {
    "arrow-clockwise": "4a78a777b692cb74826e3071ae7049cc0d3927e68a7b8272e7251b1760cd771f",
    "arrow-line-down": "fbf0ae026f34d541f48d457b34a08eab3da18739643886da298a034313ba1fb7",
    "arrows-clockwise": "e04885c469577dcfbe872f02f146b0f20468bed072959a1175913f4bdb8bc8be",
    "caret-down": "53f0cf2d0b144ac3cb07e353e0cd0853ffb3ed432d741460ce288edfcb0924b1",
    "check": "cbb89a8c42f283d4f846eb935e05d88c1be04462f16480e068c0f82b9ce40b12",
    "corners-in": "31e9060aafdd6e24f4b8aef7742a89e7e616e6e2da07160691d020cb5ab07dee",
    "corners-out": "fd79277404b3662b50b737127fb6bf6f63ae93c9b60d9f2aca9122a950ec08c8",
    "gear": "5c0cb3ba307c72babf78d952d9140c682ff30307fedc7fbb87c9a293c1e2fd64",
    "info": "438a1f319d53d86e840af73fb7b1d224c9224a437b9fa449a4e2cb45a82a3552",
    "list": "fb1851842c74b40a3f42ce3d6ae0e67ede556b624218e79c0f4ead7d9ceec51f",
    "magnifying-glass": "f9fa2561978a9a3291e8ae0ebfcb651104f5069fade8f84871627bca68889cde",
    "microphone-stage": "aca9c779a4d9df6e02468118d5d26c7e9878807ee8ac0f0be1ab566fe27a3dfa",
    "monitor": "ac9005f7dd981b06b7c029b3625bf908ded63e0dc52e781ad550ae2ef621f86a",
    "music-note": "433d99d789fa7f1f43fe9e75e68eb3c546ef7ea7a4c6545c73f2f48f32f376fa",
    "music-notes": "e40b3381d49b45275c8875d7e85f2332a11eb467f7ad9e7c9b4656c29a97e9e1",
    "palette": "f16925171ff434d8e8768da0d06867d77cd50d9e78932ab12af65bccac7a1375",
    "pause": "7a9a8fe311234262b85c6e332f77db4ba9a59b6ca4292c41d327977cf61a2fc9",
    "pencil-simple": "999530da442f44d8cf0054364373d16150f3e082c2ac294a4988a9c3a4295c28",
    "pencil-simple-line": "1917196d436f1da1fd17acb9c0641a190f5abee18c8f8a96075562377ff0c7a6",
    "play": "885b3ed2095889c4ff063dac35dc424cb9f6c00273ab44e2f90dc9dadbee661a",
    "plus": "d688feb9dd2e41c1721d6906a05a2a42e18cd774762fe706d72b2bdfd91cddc4",
    "pulse": "9c1cc6fc4b2cd292b798294f8759c9e146b41cc5606357532fe200dd07b1323b",
    "repeat": "12cef5f8ba7074921f909838088154a6847b936b11342c28daa475c62b71df86",
    "repeat-once": "fb93f05d413b97cd3d1736004661d4669cb9c7e84bf052f9fbb062ed3f478ece",
    "shuffle": "c3fdac72b005805ddf1078b858eecb55f288d7bc34573ffae276de02373bb6f2",
    "sign-out": "5d4b41197d675e395e3b55b3faf8a9db782497feaa89034a3a4fb21bb84b58a5",
    "skip-back": "20f318558e166d5dea09fb854a1bbc199e9891b17febed640e912a6ee4712d37",
    "skip-forward": "52f1ed5a89f1a38bf8537b38bd2da0501e396fc4cb03a2cdde38d248ceb03fbe",
    "speaker-high": "caca5fc1ee8489ac19232301d2c96f6d4048802491d75d761bbb89d5c98e459d",
    "swap": "8d9a481c5042552cede51fff91e460bba77b466da65e326a51f7247de857fd28",
    "text-align-left": "4d166977720d3beb386288a8607fbb36134fef06fdd99496ab09cf2d2da211b5",
    "trash": "e6a830c0409f9e101e3695c981eef98427d39fd01f502b9cbd6900930b01f19c",
    "user": "787434f7fef95f20e137d309d3c6b8b471548c55c9dfc137ae10625d70959d11",
    "x": "7397b15e1fa66eaf6ff9b59fe91d483cb5c3bcf75b2fe052097ab6f0593b513a",
}

# CantusIconKind member -> (Phosphor icon, where Cantus uses it)
ICONS = {
    "Brand": ("speaker-high", "Brand mark in the header"),
    "Play": ("play", "Play button and the song lookup Play action"),
    "Pause": ("pause", "Pause button"),
    "SkipBack": ("skip-back", "Previous track"),
    "SkipForward": ("skip-forward", "Next track"),
    "Shuffle": ("shuffle", "Shuffle toggle"),
    "Repeat": ("repeat", "Repeat off or repeat the whole context"),
    "RepeatOnce": ("repeat-once", "Repeat the current track"),
    "Volume": ("speaker-high", "Volume slider"),
    "MusicNote": ("music-note", "Lyrics tab, missing artwork, empty lyrics, instrumental break"),
    "NowPlaying": ("music-notes", "Track tab on phones"),
    "SpotifyConnect": ("music-notes", "Connect Spotify buttons"),
    "SpotifyConnected": ("check", "Connect button once a Spotify account is signed in"),
    "Karaoke": ("microphone-stage", "Karaoke underline toggle"),
    "SyncedLyrics": ("arrows-clockwise", "Switch to live synced lyrics"),
    "StaticLyrics": ("text-align-left", "Switch to static plain lyrics"),
    "ResumeAutoScroll": ("arrow-line-down", "Resume autoscroll pill"),
    "Check": ("check", "Autoscroll on"),
    "Close": ("x", "Autoscroll off, No lyrics"),
    "Search": ("magnifying-glass", "Find a song by lyrics"),
    "Palette": ("palette", "Theme menu and theme toast"),
    "ChevronDown": ("caret-down", "Dropdown hint on the theme button"),
    "Diagnostics": ("pulse", "Diagnostics HUD"),
    "EnterFullScreen": ("corners-out", "Enter kiosk mode"),
    "ExitFullScreen": ("corners-in", "Exit kiosk mode"),
    "Menu": ("list", "Overflow menu on phones"),
    "SwitchView": ("swap", "Switch mobile view"),
    "Settings": ("gear", "Sync and info tab on phones"),
    "Display": ("monitor", "Paired displays and playback device"),
    "Info": ("info", "Pairing status message"),
    "Account": ("user", "Signed-in Spotify account"),
    "SignOut": ("sign-out", "Log out"),
    "Edit": ("pencil-simple", "Wrong lyrics?"),
    "Rename": ("pencil-simple-line", "Rename a display"),
    "Delete": ("trash", "Remove a display"),
    "Add": ("plus", "Add display, add to queue"),
    "Refresh": ("arrow-clockwise", "Refresh displays, re-fetch lyrics"),
}

COMMAND_ARITY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}
NUMBER = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
SEPARATORS = " \t\r\n,"


def tokenize_path(data: str) -> list[str]:
    """Splits SVG path data into commands and numbers. Arc flags are single digits that
    may be written without separators ("a8 8 0 0110 10"), so they are read one character at a time."""
    tokens: list[str] = []
    command = ""
    argument_index = 0
    position = 0
    while position < len(data):
        char = data[position]
        if char in SEPARATORS:
            position += 1
            continue
        if char.upper() in COMMAND_ARITY:
            command = char
            argument_index = 0
            tokens.append(char)
            position += 1
            continue
        if not command or command.upper() == "Z":
            raise ValueError(f"Number without a command at {position}: {data[position:position + 20]}")
        if command.upper() == "A" and argument_index % 7 in (3, 4):
            if char not in "01":
                raise ValueError(f"Arc flag must be 0 or 1 at {position}: {data[position:position + 20]}")
            tokens.append(char)
            position += 1
        else:
            match = NUMBER.match(data, position)
            if not match:
                raise ValueError(f"Unexpected character at {position}: {data[position:position + 20]}")
            tokens.append(format_number(float(match.group())))
            position = match.end()
        argument_index += 1
    return tokens


def format_number(value: float) -> str:
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return "0" if text in ("-0", "") else text


def read_path(svg: str) -> str:
    if 'viewBox="0 0 256 256"' not in svg:
        raise ValueError("Expected a 256 x 256 viewBox")
    paths = re.findall(r'<path d="([^"]+)"', svg)
    if len(paths) != 1 or re.search(r"<(?!svg|path|/)", svg) or "transform" in svg:
        raise ValueError("Expected exactly one untransformed path")
    return " ".join(tokenize_path(paths[0]))


def ensure_sources(source_dir: Path) -> None:
    wanted = {f"{name}.svg": (f"assets/regular/{name}.svg", digest) for name, digest in PHOSPHOR_SHA256.items()}
    wanted["LICENSE"] = ("LICENSE", LICENSE_SHA256)
    for file_name, (path, expected_hash) in wanted.items():
        target = source_dir / file_name
        if not target.exists():
            url = SOURCE_URL.format(commit=PHOSPHOR_COMMIT, path=path)
            print(f"Downloading {url}")
            with urllib.request.urlopen(url) as response:
                target.write_bytes(response.read())

        actual_hash = hashlib.sha256(target.read_bytes()).hexdigest()
        if actual_hash != expected_hash:
            raise SystemExit(f"{target}: SHA-256 {actual_hash} does not match {expected_hash}")


def render(paths: dict[str, str]) -> str:
    lines = [
        "// <auto-generated>",
        "// Written by scripts/generate_icons.py from Phosphor Icons (regular weight, MIT, see LICENSE-Phosphor.txt",
        f"// in this folder), phosphor-icons/core commit {PHOSPHOR_COMMIT}. Edit the script, not this file.",
        "// </auto-generated>",
        "",
        "namespace Cantus.Client.Icons;",
        "",
        "/// <summary>Every icon the client draws, named by what it means in Cantus.</summary>",
        "public enum CantusIconKind",
        "{",
    ]
    for index, (kind, (phosphor, usage)) in enumerate(ICONS.items()):
        if index:
            lines.append("")
        lines.append(f"    /// <summary>{usage}. Phosphor \"{phosphor}\".</summary>")
        lines.append(f"    {kind},")
    lines += [
        "}",
        "",
        "internal static class CantusIconPaths",
        "{",
        "    /// <summary>Space-separated SVG path data in Phosphor's 256 x 256 box.</summary>",
        "    public static string Get(CantusIconKind kind) => kind switch",
        "    {",
    ]
    for phosphor in sorted({phosphor for phosphor, _ in ICONS.values()}):
        kinds = [kind for kind, (name, _) in ICONS.items() if name == phosphor]
        pattern = " or ".join(f"CantusIconKind.{kind}" for kind in kinds)
        lines.append(f"        {pattern} =>")
        lines.append(f"            \"{paths[phosphor]}\",")
    lines += [
        "        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),",
        "    };",
        "}",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--source-dir", type=Path, help="Folder that holds (or receives) the Phosphor SVGs and LICENSE")
    args = parser.parse_args()

    unknown = {phosphor for phosphor, _ in ICONS.values()} ^ set(PHOSPHOR_SHA256)
    if unknown:
        raise SystemExit(f"ICONS and PHOSPHOR_SHA256 disagree on: {sorted(unknown)}")

    with tempfile.TemporaryDirectory() as temp_dir:
        source_dir = args.source_dir or Path(temp_dir)
        source_dir.mkdir(parents=True, exist_ok=True)
        ensure_sources(source_dir)

        paths = {name: read_path((source_dir / f"{name}.svg").read_text(encoding="utf-8")) for name in PHOSPHOR_SHA256}
        OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
        OUTPUT_FILE.write_text(render(paths), encoding="utf-8")
        license_text = (source_dir / "LICENSE").read_text(encoding="utf-8")
        LICENSE_FILE.write_text(
            "Phosphor Icons (CantusIcons.g.cs: regular-weight path data written by scripts/generate_icons.py)\n\n"
            + license_text,
            encoding="utf-8",
        )

    print(f"{OUTPUT_FILE.relative_to(ROOT_DIR)}: {len(ICONS)} kinds, {len(paths)} paths, {OUTPUT_FILE.stat().st_size} bytes")


if __name__ == "__main__":
    main()
