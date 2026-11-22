#!/usr/bin/env python3
"""
scripts/generate_font_instances.py
Writes the static Fraunces and Figtree font files used by the client
(src/Cantus.Client/Cantus.Client/Assets/Fonts/).

google/fonts only ships these families as variable fonts. The client needs one
static file per weight (see "Fonts" in docs/architecture/client-uno.md), so this
script pins each variable axis with fontTools varLib.instancer, then subsets the
result to Latin, Latin-1, Latin Extended-A and common punctuation. Other scripts
(CJK, Cyrillic, ...) fall back to system fonts on both renderers.

Requires fontTools (pip install fonttools). The sources are downloaded from a
pinned google/fonts commit and checked against their SHA-256, or read from
--source-dir when they are already on disk.

Usage: python scripts/generate_font_instances.py [--source-dir DIR]
"""

import argparse
import hashlib
import tempfile
import urllib.request
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT_DIR = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT_DIR / "src" / "Cantus.Client" / "Cantus.Client" / "Assets" / "Fonts"

GOOGLE_FONTS_COMMIT = "06bfe0d62a98375cb09c942fa18e68bd32cff057"
SOURCE_URL = "https://raw.githubusercontent.com/google/fonts/{commit}/ofl/{path}"

SOURCES = {
    "Figtree[wght].ttf": (
        "figtree/Figtree%5Bwght%5D.ttf",
        "26ad3db9b31ff7dde67a91ff515d022d2f495cd506590699cf264f0bfe6fb714",
    ),
    "Fraunces[SOFT,WONK,opsz,wght].ttf": (
        "fraunces/Fraunces%5BSOFT%2CWONK%2Copsz%2Cwght%5D.ttf",
        "177ff6c0f14e5550a3c624247cd1189611d4eb65d000b14944c63d967958abbb",
    ),
}

# Fraunces is only used for large text (lyrics, titles, headings), so the
# optical size is pinned to a display cut. SOFT and WONK keep their defaults.
FRAUNCES_AXES = {"opsz": 72, "SOFT": 0, "WONK": 1}

INSTANCES = [
    ("Figtree[wght].ttf", "Figtree-Regular.ttf", {"wght": 400}),
    ("Figtree[wght].ttf", "Figtree-Medium.ttf", {"wght": 500}),
    ("Figtree[wght].ttf", "Figtree-SemiBold.ttf", {"wght": 600}),
    ("Fraunces[SOFT,WONK,opsz,wght].ttf", "Fraunces-Regular.ttf", {**FRAUNCES_AXES, "wght": 400}),
    ("Fraunces[SOFT,WONK,opsz,wght].ttf", "Fraunces-SemiBold.ttf", {**FRAUNCES_AXES, "wght": 600}),
]

# The instancer names the Fraunces instances "Fraunces 72pt" after the pinned
# optical size. "72pt" is not a weight, width or slope term, so DirectWrite
# (Windows head) would read the family as "Fraunces 72pt" and "#Fraunces" in
# App.xaml would not match. These outputs get plain "Fraunces" names instead.
RENAMED_STYLES = {
    "Fraunces-Regular.ttf": ("Fraunces", "Regular"),
    "Fraunces-SemiBold.ttf": ("Fraunces", "SemiBold"),
}

UNICODE_RANGES = [
    (0x0020, 0x007E),  # Basic Latin
    (0x00A0, 0x00FF),  # Latin-1 Supplement
    (0x0100, 0x017F),  # Latin Extended-A
    (0x2010, 0x2027),  # Dashes, quotes, bullets, ellipsis
    (0x2030, 0x203A),  # Per mille, primes, angle quotes
    (0x20AC, 0x20AC),  # Euro
    (0x2122, 0x2122),  # Trade mark
    (0x2190, 0x2193),  # Arrows
    (0x2212, 0x2212),  # Minus
]


def ensure_sources(source_dir: Path) -> None:
    for name, (path, expected_hash) in SOURCES.items():
        target = source_dir / name
        if not target.exists():
            url = SOURCE_URL.format(commit=GOOGLE_FONTS_COMMIT, path=path)
            print(f"Downloading {url}")
            with urllib.request.urlopen(url) as response:
                target.write_bytes(response.read())

        actual_hash = hashlib.sha256(target.read_bytes()).hexdigest()
        if actual_hash != expected_hash:
            raise SystemExit(f"{target}: SHA-256 {actual_hash} does not match {expected_hash}")


def set_style_names(font: TTFont, family: str, style: str) -> None:
    name_table = font["name"]
    version = name_table.getDebugName(3).split(";")[:2]
    postscript_name = f"{family}-{style}".replace(" ", "")
    names = {
        1: family if style == "Regular" else f"{family} {style}",
        2: "Regular",
        3: ";".join([*version, postscript_name]),
        4: f"{family} {style}",
        6: postscript_name,
        16: family,
        17: style,
    }
    for record in list(name_table.names):
        if record.nameID in names:
            name_table.removeNames(nameID=record.nameID, platformID=record.platformID)
    for name_id, value in names.items():
        name_table.setName(value, name_id, 3, 1, 0x409)


def build_instance(source: Path, axes: dict, output: Path) -> None:
    font = TTFont(source)
    static = instancer.instantiateVariableFont(font, axes, updateFontNames=True)
    for table in ("DSIG", "STAT"):
        if table in static:
            del static[table]
    if output.name in RENAMED_STYLES:
        set_style_names(static, *RENAMED_STYLES[output.name])

    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.notdef_outline = True
    options.hinting = False
    options.desubroutinize = True

    subsetter = subset.Subsetter(options=options)
    subsetter.populate(unicodes=[cp for start, end in UNICODE_RANGES for cp in range(start, end + 1)])
    subsetter.subset(static)
    static.save(output)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--source-dir", type=Path, help="Folder that holds (or receives) the variable sources")
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as temp_dir:
        source_dir = args.source_dir or Path(temp_dir)
        source_dir.mkdir(parents=True, exist_ok=True)
        ensure_sources(source_dir)

        OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
        for source_name, output_name, axes in INSTANCES:
            output = OUTPUT_DIR / output_name
            build_instance(source_dir / source_name, axes, output)
            print(f"{output.relative_to(ROOT_DIR)}: {output.stat().st_size} bytes")


if __name__ == "__main__":
    main()
