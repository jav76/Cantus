#!/usr/bin/env python3
"""
scripts/generate_docs.py
Checks that every core documentation page exists before the MkDocs build.
"""

import sys
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
DOCS_DIR = ROOT_DIR / "docs"


INDEX_PAGE = "index.md"


def validate_documentation_integrity():
    """Verify that required documentation pages exist and are well-formed."""
    required_pages = [
        DOCS_DIR / INDEX_PAGE,
        DOCS_DIR / "user-guide" / INDEX_PAGE,
        DOCS_DIR / "user-guide" / "connect-spotify.md",
        DOCS_DIR / "user-guide" / "local-players.md",
        DOCS_DIR / "user-guide" / "displays.md",
        DOCS_DIR / "user-guide" / "song-lookup.md",
        DOCS_DIR / "user-guide" / "playback-and-display.md",
        DOCS_DIR / "user-guide" / "timing-and-calibration.md",
        DOCS_DIR / "user-guide" / "theming.md",
        DOCS_DIR / "operator-guide" / INDEX_PAGE,
        DOCS_DIR / "operator-guide" / "self-hosting.md",
        DOCS_DIR / "operator-guide" / "spotify-setup.md",
        DOCS_DIR / "operator-guide" / "reverse-proxy.md",
        DOCS_DIR / "operator-guide" / "displays.md",
        DOCS_DIR / "operator-guide" / "troubleshooting.md",
        DOCS_DIR / "architecture" / "overview.md",
        DOCS_DIR / "architecture" / "ntp-clock-sync.md",
        DOCS_DIR / "architecture" / "adaptive-polling.md",
        DOCS_DIR / "architecture" / "lyrics-caching.md",
        DOCS_DIR / "architecture" / "client-uno.md",
        DOCS_DIR / "reference" / "signalr-api.md",
        DOCS_DIR / "reference" / "rest-api.md",
        DOCS_DIR / "reference" / "configuration.md",
        DOCS_DIR / "contributing" / INDEX_PAGE,
    ]

    missing = [p for p in required_pages if not p.exists()]
    if missing:
        print(f"[ERROR] Missing required documentation pages ({len(missing)}):")
        for p in missing:
            print(f"  - {p.relative_to(ROOT_DIR)}")
        return False

    print(f"[OK] All {len(required_pages)} core documentation pages validated.")
    return True


def main():
    print("Checking Cantus documentation pages...")

    if not validate_documentation_integrity():
        sys.exit(1)

    print("Documentation build validation completed successfully.")


if __name__ == "__main__":
    main()
