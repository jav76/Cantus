#!/usr/bin/env python3
"""
scripts/generate_app_icons.py
Renders the desktop app icon files from packaging/icons/cantus.svg, the same
artwork as the web app's tab icon (WasmScripts/index.html):

  packaging/icons/cantus.png   256 x 256, for the window icon and the AppImage
  packaging/icons/cantus.ico   16 to 256 px, for the Windows executable and installer

Needs librsvg and cairo through PyGObject (Debian: python3-gi python3-cairo
gir1.2-rsvg-2.0). Run it again after changing the SVG and commit the results.

Usage: python scripts/generate_app_icons.py
"""

import io
import struct
from pathlib import Path

import cairo
import gi

gi.require_version("Rsvg", "2.0")
from gi.repository import Rsvg  # noqa: E402

ICON_DIR = Path(__file__).resolve().parent.parent / "packaging" / "icons"
SOURCE_FILE = ICON_DIR / "cantus.svg"
PNG_SIZE = 256
ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)


def render_png(handle: Rsvg.Handle, size: int) -> bytes:
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    viewport = Rsvg.Rectangle()
    viewport.x = viewport.y = 0
    viewport.width = viewport.height = size
    handle.render_document(cairo.Context(surface), viewport)
    buffer = io.BytesIO()
    surface.write_to_png(buffer)
    return buffer.getvalue()


def build_ico(images: dict[int, bytes]) -> bytes:
    # ICONDIR, then one 16-byte ICONDIRENTRY per image, then the PNG payloads.
    # A width or height byte of 0 means 256.
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries = b""
    payload = b""
    for size, png in images.items():
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png), offset)
        payload += png
        offset += len(png)
    return header + entries + payload


def main() -> None:
    handle = Rsvg.Handle.new_from_file(str(SOURCE_FILE))
    (ICON_DIR / "cantus.png").write_bytes(render_png(handle, PNG_SIZE))
    (ICON_DIR / "cantus.ico").write_bytes(build_ico({size: render_png(handle, size) for size in ICO_SIZES}))
    print(f"Wrote cantus.png and cantus.ico to {ICON_DIR}")


if __name__ == "__main__":
    main()
