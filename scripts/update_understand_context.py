#!/usr/bin/env python3
"""
scripts/update_understand_context.py
Refreshes and synchronizes all Understand-Anything context files in .ua/
including fingerprints, scan results, knowledge graph, domain graph, and metadata,
then triggers deterministic documentation generation.
"""

import datetime
import fnmatch
import hashlib
import json
import os
import re
import subprocess
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

ROOT_DIR = Path(__file__).resolve().parent.parent
UA_DIR = ROOT_DIR / ".ua"
INTERMEDIATE_DIR = UA_DIR / "intermediate"

EXCLUDED_DIR_NAMES = {
    ".git", ".ua", "tests", "graphify-out", "artifacts", "all_artifacts",
    "wasm_dist", "server_dist", "bin", "obj", "node_modules", "site", ".trash-1787498678"
}


def get_git_commit() -> str:
    try:
        return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT_DIR).decode().strip()
    except Exception:
        return "HEAD"


def get_current_iso_timestamp() -> str:
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def is_path_excluded(p: Path) -> bool:
    rel = p.relative_to(ROOT_DIR)
    if str(rel) == ".ua/.understandignore":
        return False
    parts = set(rel.parts)
    if parts & EXCLUDED_DIR_NAMES:
        return True
    rel_str = str(rel)
    if rel_str.startswith("packaging/windows/Output") or rel_str.startswith("packaging/linux/AppDir"):
        return True
    name = p.name
    if name == ".env" or name.startswith(".env."):
        return True
    if name.endswith("Tests.cs") or name.endswith("Test.cs") or name.endswith("Fixture.cs") or name.endswith(".Tests.csproj"):
        return True
    if name in {"secrets.json", "cantus.db", "cantus.db-shm", "cantus.db-wal"} or name.endswith((".pfx", ".p12", ".snk", ".key")):
        return True
    if name.startswith("appsettings.") and (name.endswith(".Local.json") or name.endswith(".Secrets.json")):
        return True
    return False


def get_language_and_category(rel_str: str) -> Tuple[str, str]:
    p = Path(rel_str)
    name = p.name
    ext = p.suffix.lower()

    if ext == ".cs":
        return "csharp", "code"
    elif ext == ".xaml":
        return "xaml", "code"
    elif ext == ".csproj":
        return "csproj", "config"
    elif ext == ".props":
        return "props", "config"
    elif ext == ".slnx":
        return "slnx", "config"
    elif ext == ".json":
        return "json", "config"
    elif ext in [".yml", ".yaml"]:
        return "yaml", "infra"
    elif ext == ".md":
        return "markdown", "docs"
    elif ext == ".html":
        return "html", "code"
    elif ext == ".py":
        return "python", "script"
    elif ext == ".iss":
        return "iss", "infra"
    elif ext == ".sh" or name == "AppRun":
        return "shell", "infra"
    elif ext == ".desktop":
        return "desktop", "infra"
    elif name == "Dockerfile":
        return "dockerfile", "infra"
    elif name in [".dockerignore", ".gitignore", ".understandignore"]:
        return "unknown", "infra"
    elif name == "LICENSE":
        return "markdown", "docs"
    return "unknown", "code"


def parse_csharp_structure(content: str) -> Dict[str, Any]:
    lines = content.splitlines()
    total_lines = len(lines)

    # Extract usings / imports
    imports = []
    for m in re.finditer(r"^\s*using\s+([^;]+);", content, re.MULTILINE):
        src = m.group(1).strip()
        spec = src.split(".")[-1]
        imports.append({"source": src, "specifiers": [spec]})

    # Extract classes / interfaces / records
    classes = []
    exports = set()
    class_matches = list(re.finditer(r"(public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+)?(class|interface|record|enum|struct)\s+(\w+)", content))

    for cm in class_matches:
        vis = cm.group(1) or "internal"
        cname = cm.group(3)
        if vis in ("public", "internal"):
            exports.add(cname)

        # Methods within file
        methods = []
        method_matches = re.finditer(r"(public|internal|private|protected)\s+(?:async\s+)?(?:Task(?:<[\w\?<>]+>)?|ValueTask(?:<[\w\?<>]+>)?|void|string|int|bool|TimeSpan|Uri|IReadOnlyList<[\w\?<>]+>|double|long|int\?)\s+(\w+)\s*\(", content)
        for mm in method_matches:
            mname = mm.group(2)
            if mname not in methods:
                methods.append(mname)
                if mm.group(1) in ("public", "internal"):
                    exports.add(mname)

        # Properties
        props = []
        prop_matches = re.finditer(r"(public|internal|private|protected)\s+(?:required\s+)?[\w\?<>,\[\]]+\s+(\w+)\s*\{\s*get;", content)
        for pm in prop_matches:
            pname = pm.group(2)
            if pname not in props:
                props.append(pname)
                if pm.group(1) in ("public", "internal"):
                    exports.add(pname)

        classes.append({
            "name": cname,
            "methods": methods,
            "properties": props,
            "exported": vis in ("public", "internal"),
            "lineCount": total_lines
        })

    # Standalone public functions
    functions = []
    func_matches = re.finditer(r"(public|internal|private)\s+(?:static\s+)?(?:async\s+)?([\w\?<>\[\]]+)\s+(\w+)\s*\(([^)]*)\)", content)
    for fm in func_matches:
        fvis = fm.group(1)
        fret = fm.group(2)
        fname = fm.group(3)
        fparams = [p.strip().split()[-1] for p in fm.group(4).split(",") if p.strip()]
        if fname not in ("if", "switch", "while", "for", "foreach", "catch"):
            functions.append({
                "name": fname,
                "params": fparams,
                "returnType": fret,
                "exported": fvis in ("public", "internal"),
                "lineCount": 10
            })
            if fvis in ("public", "internal"):
                exports.add(fname)

    return {
        "functions": functions[:10],
        "classes": classes,
        "imports": imports,
        "exports": sorted(list(exports)),
        "totalLines": total_lines,
        "hasStructuralAnalysis": True
    }


def update_fingerprints(all_files: List[Path], commit_hash: str, timestamp: str) -> Dict[str, Any]:
    fp_path = UA_DIR / "fingerprints.json"
    old_fp = json.loads(fp_path.read_text(encoding="utf-8")) if fp_path.exists() else {}
    old_files = old_fp.get("files", {})

    new_files_map = {}
    for p in all_files:
        rel = str(p.relative_to(ROOT_DIR))
        raw_bytes = p.read_bytes()
        sha = hashlib.sha256(raw_bytes).hexdigest()
        try:
            content = raw_bytes.decode("utf-8")
            lines_count = len(content.splitlines())
        except UnicodeDecodeError:
            content = ""
            lines_count = 0

        lang, cat = get_language_and_category(rel)

        if lang == "csharp" and content:
            structure = parse_csharp_structure(content)
            new_files_map[rel] = {
                "filePath": rel,
                "contentHash": sha,
                "functions": structure["functions"],
                "classes": structure["classes"],
                "imports": structure["imports"],
                "exports": structure["exports"],
                "totalLines": lines_count,
                "hasStructuralAnalysis": True
            }
        else:
            old_entry = old_files.get(rel, {})
            new_files_map[rel] = {
                "filePath": rel,
                "contentHash": sha,
                "functions": old_entry.get("functions", []),
                "classes": old_entry.get("classes", []),
                "imports": old_entry.get("imports", []),
                "exports": old_entry.get("exports", []),
                "totalLines": lines_count,
                "hasStructuralAnalysis": lang in ("csharp", "yaml", "markdown", "xaml", "html")
            }

    fingerprints = {
        "version": "1.0.0",
        "gitCommitHash": commit_hash,
        "generatedAt": timestamp,
        "files": new_files_map
    }

    fp_path.write_text(json.dumps(fingerprints, indent=2), encoding="utf-8")
    print(f"Updated fingerprints.json ({len(new_files_map)} files)")
    return fingerprints


def update_scan_result(all_files: List[Path], commit_hash: str) -> Dict[str, Any]:
    INTERMEDIATE_DIR.mkdir(parents=True, exist_ok=True)
    scan_path = INTERMEDIATE_DIR / "scan-result.json"

    file_entries = []
    languages = set()
    import_map = {}

    for p in all_files:
        rel = str(p.relative_to(ROOT_DIR))
        lines_count = len(p.read_text(encoding="utf-8", errors="ignore").splitlines())
        lang, cat = get_language_and_category(rel)
        languages.add(lang)

        file_entries.append({
            "path": rel,
            "language": lang,
            "sizeLines": lines_count,
            "fileCategory": cat
        })
        import_map[rel] = []

    scan_result = {
        "name": "Cantus",
        "description": "Cantus is a self-hosted, multi-room synchronized lyrics display platform integrated with Spotify playback via ASP.NET Core, SignalR, and Uno Platform.",
        "languages": sorted(list(languages)),
        "frameworks": [
            "ASP.NET Core",
            "Entity Framework Core",
            "SignalR",
            "Uno Platform",
            "Docker",
            "Docker Compose",
            "GitHub Actions"
        ],
        "files": file_entries,
        "totalFiles": len(file_entries),
        "filteredByIgnore": 0,
        "estimatedComplexity": "moderate",
        "importMap": import_map
    }

    scan_path.write_text(json.dumps(scan_result, indent=2), encoding="utf-8")
    print(f"Updated scan-result.json ({len(file_entries)} files)")
    return scan_result


def update_meta(file_count: int, commit_hash: str, timestamp: str) -> Dict[str, Any]:
    meta_path = UA_DIR / "meta.json"
    meta = {
        "lastAnalyzedAt": timestamp,
        "gitCommitHash": commit_hash,
        "version": "1.0.0",
        "analyzedFiles": file_count
    }
    meta_path.write_text(json.dumps(meta, indent=2), encoding="utf-8")
    print(f"Updated meta.json ({file_count} files analyzed at {timestamp})")
    return meta


def update_knowledge_graph(all_files: List[Path], commit_hash: str, timestamp: str) -> Dict[str, Any]:
    kg_path = UA_DIR / "knowledge-graph.json"
    kg = json.loads(kg_path.read_text(encoding="utf-8"))

    kg["project"]["analyzedAt"] = timestamp
    kg["project"]["gitCommitHash"] = commit_hash

    # Fix moved ADR and implementation plan paths in existing nodes
    path_remaps = {
        "docs/architecture/ADR-001-spotify-synced-lyrics-platform.md": "docs/local/architecture/ADR-001-spotify-synced-lyrics-platform.md",
        "docs/architecture/ADR-002-docker-containerization-and-host-configuration.md": "docs/local/architecture/ADR-002-docker-containerization-and-host-configuration.md",
        "docs/architecture/ADR-003-ci-cd-and-release-packaging.md": "docs/local/architecture/ADR-003-ci-cd-and-release-packaging.md",
        "docs/ImplementationPlan.md": "docs/local/ImplementationPlan.md"
    }

    nodes = kg.get("nodes", [])
    existing_file_paths = set()
    for n in nodes:
        old_fp = n.get("filePath")
        if old_fp in path_remaps:
            new_fp = path_remaps[old_fp]
            n["filePath"] = new_fp
            n["id"] = f"document:{new_fp}"
            n["name"] = Path(new_fp).name
        if n.get("filePath"):
            existing_file_paths.add(n.get("filePath"))

    # Add missing documentation / configuration files as nodes
    devops_nodes = []
    for p in all_files:
        rel = str(p.relative_to(ROOT_DIR))
        if rel not in existing_file_paths:
            lang, cat = get_language_and_category(rel)
            if cat == "docs":
                ntype = "document"
            elif cat == "config":
                ntype = "config"
            elif cat == "infra":
                if rel.startswith(".github/workflows"):
                    ntype = "pipeline"
                elif "docker" in rel:
                    ntype = "service"
                else:
                    ntype = "file"
            elif cat == "script":
                ntype = "file"
            else:
                ntype = "file"

            node_id = f"{ntype}:{rel}"
            summary = f"Project {cat} asset for {p.name}."
            if "quickstart" in rel:
                summary = "Quickstart onboarding guide for Cantus self-hosting and client connection."
            elif "self-hosting" in rel:
                summary = "Operator guide detailing self-hosted Docker and bare-metal deployment procedures."
            elif "spotify-setup" in rel:
                summary = "Operator guide for configuring Spotify Developer Dashboard and OAuth PKCE redirect URIs."
            elif "troubleshooting" in rel:
                summary = "Operational runbook for diagnosing SignalR connectivity, NTP skew, and Spotify token renewal."
            elif "generate_docs" in rel:
                summary = "Deterministic markdown generator producing architecture and domain flow documentation from .ua graphs."
            elif "keysetup" in rel:
                summary = "Local key management and cryptographic secret setup instructions."
            elif "mkdocs.yml" in rel:
                summary = "MkDocs Material site configuration, navigation structure, and markdown extension setup."
            elif "docker-compose.docs.yml" in rel:
                summary = "Docker Compose manifest for previewing MkDocs Material documentation locally."
            elif "understand-context" in rel:
                summary = "Understand-Anything agent rule defining architecture layers and domain flow guidelines."

            new_node = {
                "id": node_id,
                "type": ntype,
                "name": p.name,
                "filePath": rel,
                "summary": summary,
                "tags": [cat, lang],
                "complexity": "simple"
            }
            nodes.append(new_node)
            devops_nodes.append(node_id)
            existing_file_paths.add(rel)

    kg["nodes"] = nodes

    # Update layer:devops-config nodeIds
    layers = kg.get("layers", [])
    for l in layers:
        if l["id"] == "layer:devops-config":
            current_ids = set(l.get("nodeIds", []))
            # Clean up old ids
            for old_p, new_p in path_remaps.items():
                old_id = f"document:{old_p}"
                new_id = f"document:{new_p}"
                if old_id in current_ids:
                    current_ids.remove(old_id)
                    current_ids.add(new_id)
            # Add new devops nodes
            current_ids.update(devops_nodes)
            l["nodeIds"] = sorted(list(current_ids))

    kg_path.write_text(json.dumps(kg, indent=2), encoding="utf-8")
    print(f"Updated knowledge-graph.json ({len(nodes)} nodes, {len(kg.get('edges', []))} edges)")
    return kg


def update_domain_graph(commit_hash: str, timestamp: str) -> Dict[str, Any]:
    dg_path = UA_DIR / "domain-graph.json"
    dg = json.loads(dg_path.read_text(encoding="utf-8"))

    dg["project"]["analyzedAt"] = timestamp
    dg["project"]["gitCommitHash"] = commit_hash

    # Exact step line calibrations based on current source implementations
    step_calibrations = {
        "step:spotify-pkce-login:generate-pkce-challenge": ("src/Cantus.Server/Services/PkceHelper.cs", [10, 28]),
        "step:spotify-pkce-login:redirect-spotify-auth": ("src/Cantus.Infrastructure/Spotify/SpotifyAuthService.cs", [32, 44]),
        "step:spotify-pkce-login:exchange-auth-code": ("src/Cantus.Infrastructure/Spotify/SpotifyAuthService.cs", [46, 108]),
        "step:spotify-pkce-login:persist-encrypted-session": ("src/Cantus.Infrastructure/Security/DataProtectionTokenEncryptionService.cs", [15, 34]),
        "step:token-refresh-lifecycle:check-token-expiration": ("src/Cantus.Infrastructure/Spotify/SpotifyAuthService.cs", [110, 150]),
        "step:token-refresh-lifecycle:refresh-tokens-via-spotify": ("src/Cantus.Infrastructure/Spotify/SpotifyAuthService.cs", [115, 149]),
        "step:token-refresh-lifecycle:update-stored-session": ("src/Cantus.Infrastructure/Persistence/CantusDbContext.cs", [1, 51]),
        "step:adaptive-playback-polling:evaluate-active-listeners": ("src/Cantus.Server/Services/PlaybackSessionRegistry.cs", [18, 59]),
        "step:adaptive-playback-polling:query-spotify-playback": ("src/Cantus.Infrastructure/Spotify/SpotifyPlayerClient.cs", [17, 95]),
        "step:adaptive-playback-polling:detect-state-transitions": ("src/Cantus.Server/BackgroundServices/ActiveUsersPlaybackMonitor.cs", [71, 215]),
        "step:adaptive-playback-polling:broadcast-playback-update": ("src/Cantus.Server/Hubs/PlaybackHub.cs", [28, 90]),
        "step:fetch-and-cache-lyrics:check-sqlite-cache": ("src/Cantus.Infrastructure/Lyrics/SqliteLyricsCacheRepository.cs", [20, 72]),
        "step:fetch-and-cache-lyrics:fetch-external-lrclib": ("src/Cantus.Infrastructure/Lyrics/LrclibLyricsProvider.cs", [37, 128]),
        "step:fetch-and-cache-lyrics:parse-lrc-timestamps": ("src/Cantus.Core/Parsers/LrcParser.cs", [8, 164]),
        "step:fetch-and-cache-lyrics:save-lyrics-cache": ("src/Cantus.Infrastructure/Lyrics/SqliteLyricsCacheRepository.cs", [94, 194]),
        "step:track-latency-offset-adjustment:receive-offset-nudge": ("src/Cantus.Server/Hubs/PlaybackHub.cs", [143, 172]),
        "step:track-latency-offset-adjustment:persist-track-offset": ("src/Cantus.Infrastructure/Lyrics/SqliteLyricsCacheRepository.cs", [209, 237]),
        "step:track-latency-offset-adjustment:broadcast-offset-sync": ("src/Cantus.Server/Hubs/PlaybackHub.cs", [165, 171]),
        "step:ntp-clock-synchronization:send-ntp-ping": ("src/Cantus.Client/Cantus.Client/Services/SignalRPlaybackClient.cs", [140, 165]),
        "step:ntp-clock-synchronization:server-ntp-timestamp": ("src/Cantus.Server/Hubs/PlaybackHub.cs", [100, 109]),
        "step:ntp-clock-synchronization:compute-skew-and-rtt": ("src/Cantus.Client/Cantus.Client/Services/SignalRPlaybackClient.cs", [167, 215]),
        "step:real-time-lyrics-scrolling:interpolate-playback-clock": ("src/Cantus.Infrastructure/Clock/PlaybackInterpolator.cs", [23, 115]),
        "step:real-time-lyrics-scrolling:calculate-active-lyric-line": ("src/Cantus.Core/Models/SyncedLyrics.cs", [14, 46]),
        "step:real-time-lyrics-scrolling:update-ui-and-theme": ("src/Cantus.Client/Cantus.Client/ViewModels/LyricsViewModel.cs", [378, 515]),
    }

    for n in dg.get("nodes", []):
        nid = n.get("id")
        if nid in step_calibrations:
            fpath, lrange = step_calibrations[nid]
            n["filePath"] = fpath
            n["lineRange"] = lrange

    dg_path.write_text(json.dumps(dg, indent=2), encoding="utf-8")
    print(f"Updated domain-graph.json ({len(dg.get('nodes', []))} nodes, {len(dg.get('edges', []))} edges)")
    return dg


def run_generate_docs():
    gen_script = ROOT_DIR / "scripts" / "generate_docs.py"
    subprocess.check_call(["python3", str(gen_script)], cwd=ROOT_DIR)


def main():
    print("=== Updating Understand-Anything Context ===")
    commit_hash = get_git_commit()
    timestamp = get_current_iso_timestamp()

    all_files = sorted([p for p in ROOT_DIR.rglob("*") if p.is_file() and not is_path_excluded(p)])
    print(f"Discovered {len(all_files)} eligible files across the repository.")

    update_scan_result(all_files, commit_hash)
    update_meta(len(all_files), commit_hash, timestamp)
    update_knowledge_graph(all_files, commit_hash, timestamp)
    update_domain_graph(commit_hash, timestamp)

    print("=== Generating Documentation from Fresh Graphs ===")
    run_generate_docs()

    print("=== Synchronizing Final Fingerprints ===")
    # Refresh all files including freshly generated docs
    final_files = sorted([p for p in ROOT_DIR.rglob("*") if p.is_file() and not is_path_excluded(p)])
    update_fingerprints(final_files, commit_hash, timestamp)

    print("=== Understand-Anything Context Update Complete ===")


if __name__ == "__main__":
    main()
