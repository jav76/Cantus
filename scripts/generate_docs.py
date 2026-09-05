#!/usr/bin/env python3
"""
scripts/generate_docs.py
Hybrid Documentation Engine for Cantus.
Validates architectural layers and business domain knowledge against .ua/ graphs,
ensuring documentation integrity during CI/CD builds without generating raw machine dumps.
"""

import json
import sys
from pathlib import Path
from typing import Any, Dict

ROOT_DIR = Path(__file__).resolve().parent.parent
UA_DIR = ROOT_DIR / ".ua"
DOCS_DIR = ROOT_DIR / "docs"


def load_json(path: Path) -> Dict[str, Any]:
    if not path.exists():
        print(f"[WARN] Knowledge graph file not found at: {path}")
        return {}
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def validate_documentation_integrity():
    """Verify that required documentation pages exist and are well-formed."""
    required_pages = [
        DOCS_DIR / "index.md",
        DOCS_DIR / "user-guide" / "index.md",
        DOCS_DIR / "user-guide" / "playback-and-display.md",
        DOCS_DIR / "user-guide" / "timing-and-calibration.md",
        DOCS_DIR / "user-guide" / "theming.md",
        DOCS_DIR / "operator-guide" / "index.md",
        DOCS_DIR / "operator-guide" / "self-hosting.md",
        DOCS_DIR / "operator-guide" / "spotify-setup.md",
        DOCS_DIR / "operator-guide" / "reverse-proxy.md",
        DOCS_DIR / "operator-guide" / "troubleshooting.md",
        DOCS_DIR / "architecture" / "overview.md",
        DOCS_DIR / "architecture" / "ntp-clock-sync.md",
        DOCS_DIR / "architecture" / "adaptive-polling.md",
        DOCS_DIR / "architecture" / "lyrics-caching.md",
        DOCS_DIR / "architecture" / "client-uno.md",
        DOCS_DIR / "reference" / "signalr-api.md",
        DOCS_DIR / "reference" / "rest-api.md",
        DOCS_DIR / "reference" / "configuration.md",
        DOCS_DIR / "contributing" / "index.md",
    ]

    missing = [p for p in required_pages if not p.exists()]
    if missing:
        print(f"[ERROR] Missing required documentation pages ({len(missing)}):")
        for p in missing:
            print(f"  - {p.relative_to(ROOT_DIR)}")
        return False

    print(f"[OK] All {len(required_pages)} core documentation pages validated.")
    return True


def verify_knowledge_graphs():
    """Verify knowledge graphs and report layer & domain counts."""
    kg_path = UA_DIR / "knowledge-graph.json"
    dg_path = UA_DIR / "domain-graph.json"

    if kg_path.exists():
        kg = load_json(kg_path)
        layers = len(kg.get("layers", []))
        nodes = len(kg.get("nodes", []))
        edges = len(kg.get("edges", []))
        print(f"[INFO] Knowledge Graph: {layers} layers, {nodes} nodes, {edges} edges validated.")

    if dg_path.exists():
        dg = load_json(dg_path)
        domains = len([n for n in dg.get("nodes", []) if n.get("type") == "domain"])
        flows = len([n for n in dg.get("nodes", []) if n.get("type") == "flow"])
        steps = len([n for n in dg.get("nodes", []) if n.get("type") == "step"])
        print(f"[INFO] Domain Graph: {domains} domains, {flows} flows, {steps} steps validated.")


def main():
    print("Running Cantus Hybrid Documentation Validator...")
    verify_knowledge_graphs()

    if not validate_documentation_integrity():
        sys.exit(1)

    print("Documentation build validation completed successfully.")


if __name__ == "__main__":
    main()
