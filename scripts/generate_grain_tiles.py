#!/usr/bin/env python3
"""
scripts/generate_grain_tiles.py
Writes the film grain tiles used by the client's post-processing overlay
(src/Cantus.Client/Cantus.Client/Assets/PostProcessing/grain_N.png).

Each tile is a 256x256 4-bit palette PNG: about half the pixels are clear,
the rest are faint white or black specks. The seeds are fixed, so running
the script again reproduces the same files. Standard library only.
"""

import random
import struct
import zlib
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
OUTPUT_DIR = ROOT_DIR / "src" / "Cantus.Client" / "Cantus.Client" / "Assets" / "PostProcessing"

TILE_SIZE = 256
TILE_COUNT = 3
BASE_SEED = 1009
SEED_STEP = 7919
CLEAR_PIXEL_RATIO = 0.55
LIGHT_SPECK_RATIO = 0.5

# Palette index 0 is clear, 1-7 are white specks, 8-15 are black specks.
LIGHT_ALPHAS = [18, 30, 44, 60, 78, 100, 130]
DARK_ALPHAS = [18, 30, 44, 60, 78, 100, 130, 160]


def png_chunk(kind: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(kind + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", crc)


def speck_index(rng: random.Random) -> int:
    if rng.random() < CLEAR_PIXEL_RATIO:
        return 0

    # Minimum of three samples biases towards the faintest specks.
    level = int(min(rng.random(), rng.random(), rng.random()) * len(DARK_ALPHAS))
    if rng.random() < LIGHT_SPECK_RATIO:
        return 1 + min(level, len(LIGHT_ALPHAS) - 1)
    return 1 + len(LIGHT_ALPHAS) + level


def build_tile(seed: int) -> bytes:
    rng = random.Random(seed)
    rows = []
    for _ in range(TILE_SIZE):
        pixels = [speck_index(rng) for _ in range(TILE_SIZE)]
        row = bytearray([0])
        for i in range(0, TILE_SIZE, 2):
            row.append((pixels[i] << 4) | pixels[i + 1])
        rows.append(bytes(row))

    palette = bytes([0, 0, 0]) + bytes([255, 255, 255]) * len(LIGHT_ALPHAS) + bytes([0, 0, 0]) * len(DARK_ALPHAS)
    transparency = bytes([0]) + bytes(LIGHT_ALPHAS) + bytes(DARK_ALPHAS)
    header = struct.pack(">IIBBBBB", TILE_SIZE, TILE_SIZE, 4, 3, 0, 0, 0)

    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", header)
        + png_chunk(b"PLTE", palette)
        + png_chunk(b"tRNS", transparency)
        + png_chunk(b"IDAT", zlib.compress(b"".join(rows), 9))
        + png_chunk(b"IEND", b"")
    )


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for i in range(TILE_COUNT):
        path = OUTPUT_DIR / f"grain_{i}.png"
        path.write_bytes(build_tile(BASE_SEED + i * SEED_STEP))
        print(f"{path.relative_to(ROOT_DIR)}: {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
