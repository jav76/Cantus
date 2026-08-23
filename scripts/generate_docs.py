#!/usr/bin/env python3
"""
scripts/generate_docs.py
Deterministic documentation generator for Cantus.
Extracts architectural layers, component dependencies, and business domain flows
from .ua/knowledge-graph.json and .ua/domain-graph.json directly into docs/.
"""

import json
from pathlib import Path
from typing import Any, Dict, List

ROOT_DIR = Path(__file__).resolve().parent.parent
UA_DIR = ROOT_DIR / ".ua"
DOCS_DIR = ROOT_DIR / "docs"
ARCH_DIR = DOCS_DIR / "architecture"
FLOWS_DIR = DOCS_DIR / "domain-flows"


def load_json(path: Path) -> Dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(f"Missing required graph data: {path}")
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def sanitize_mermaid_label(text: str) -> str:
    return text.replace('"', "'").replace("\n", " ").strip()


def generate_architecture_overview(kg: Dict[str, Any]):
    ARCH_DIR.mkdir(parents=True, exist_ok=True)
    layers = kg.get("layers", [])
    nodes = {n["id"]: n for n in kg.get("nodes", [])}
    edges = kg.get("edges", [])

    lines = [
        "# System Architecture Overview",
        "",
        f"**Project**: {kg.get('project', {}).get('name', 'Cantus')}",
        "",
        f"**Description**: {kg.get('project', {}).get('description', '')}",
        "",
        f"**Frameworks**: {', '.join(kg.get('project', {}).get('frameworks', []))}",
        "",
        "Cantus is built upon a modular **5-Layer Architecture** designed for high-concurrency real-time streaming, sub-millisecond clock synchronization, and cross-platform desktop/browser lyric rendering.",
        "",
        "## Architectural Layers",
        "",
        "```mermaid",
        "graph TB",
    ]

    layer_order = ["layer:client-ui", "layer:server-api", "layer:core-domain", "layer:infrastructure-persistence", "layer:devops-config"]
    for lid in layer_order:
        layer = next((l for l in layers if l["id"] == lid), None)
        if layer:
            clean_name = sanitize_mermaid_label(layer["name"])
            clean_desc = sanitize_mermaid_label(layer["description"])
            lines.append(f'    {layer["id"].replace(":", "_")}["**{clean_name}**<br/>{clean_desc}"]')

    lines.extend([
        "    layer_client_ui --> layer_server_api",
        "    layer_server_api --> layer_core_domain",
        "    layer_server_api --> layer_infrastructure_persistence",
        "    layer_infrastructure_persistence --> layer_core_domain",
        "    layer_devops_config -. packages .-> layer_client_ui",
        "    layer_devops_config -. packages .-> layer_server_api",
        "```",
        "",
        "## Layer Summary",
        "",
        "| Layer | Description | Components | Page Link |",
        "| :--- | :--- | :---: | :--- |",
    ])

    slug_map = {
        "layer:client-ui": ("Client Presentation (Uno Platform)", "client.md"),
        "layer:server-api": ("Server Engine & Real-Time Hub", "server.md"),
        "layer:core-domain": ("Core Domain Contracts & Models", "core.md"),
        "layer:infrastructure-persistence": ("Infrastructure & External Services", "infrastructure.md"),
        "layer:devops-config": ("DevOps & Release Packaging", "../reference/docker.md"),
    }

    for l in layers:
        lid = l["id"]
        node_count = len(l.get("nodeIds", []))
        name, link = slug_map.get(lid, (l["name"], "overview.md"))
        lines.append(f"| **{name}** | {l.get('description', '')} | {node_count} | [{name}]({link}) |")

    lines.extend([
        "",
        "## Knowledge Graph Statistics",
        "",
        f"- **Total Extracted Nodes**: {len(nodes)}",
        f"- **Total Relationships / Edges**: {len(edges)}",
        f"- **Architectural Layers**: {len(layers)}",
        f"- **Last Analysis Timestamp**: `{kg.get('project', {}).get('analyzedAt', 'N/A')}`",
        "",
    ])

    out_file = ARCH_DIR / "overview.md"
    out_file.write_text("\n".join(lines), encoding="utf-8")
    print(f"Generated: {out_file}")


def generate_layer_docs(kg: Dict[str, Any]):
    ARCH_DIR.mkdir(parents=True, exist_ok=True)
    nodes = {n["id"]: n for n in kg.get("nodes", [])}
    layers = {l["id"]: l for l in kg.get("layers", [])}

    layer_configs = [
        {
            "id": "layer:client-ui",
            "filename": "client.md",
            "title": "Client Presentation Layer (Uno Platform)",
            "overview": "The client presentation layer provides cross-platform UI views built on Uno Platform, supporting WebAssembly (browser), Linux (Skia/X11), and Windows (Desktop). It implements the MVVM pattern with real-time SignalR subscriptions, dynamic lyric scrolling, and cover art palette blending."
        },
        {
            "id": "layer:server-api",
            "filename": "server.md",
            "title": "Server Engine & Real-Time Hub",
            "overview": "The server engine is an ASP.NET Core 9 Minimal API host that manages authenticated Spotify playback sessions, runs intelligent background polling workers, and coordinates low-latency SignalR event distribution across connected display rooms."
        },
        {
            "id": "layer:core-domain",
            "filename": "core.md",
            "title": "Core Domain Contracts & Models",
            "overview": "The core domain layer encapsulates framework-agnostic models, interfaces, and LRC parsing algorithms. It defines the contracts that govern playback state snapshots, synchronized lyric timings, provider abstractions, and clock offset calculations."
        },
        {
            "id": "layer:infrastructure-persistence",
            "filename": "infrastructure.md",
            "title": "Infrastructure & Persistence Layer",
            "overview": "The infrastructure layer handles external integrations and persistence: Spotify OAuth PKCE token exchange, EF Core SQLite caching with negative cache tracking, LRCLIB API queries with fuzzy matching, token encryption at rest via ASP.NET Core Data Protection, and sub-millisecond playback clock interpolation."
        },
    ]

    for cfg in layer_configs:
        lid = cfg["id"]
        layer = layers.get(lid)
        if not layer:
            continue

        lines = [
            f"# {cfg['title']}",
            "",
            cfg["overview"],
            "",
            "## Layer Metadata",
            "",
            f"- **Layer ID**: `{lid}`",
            f"- **Component Count**: `{len(layer.get('nodeIds', []))}`",
            f"- **Role**: {layer.get('description', '')}",
            "",
            "## Key Components & Files",
            "",
            "| Component | Type | Summary | Complexity |",
            "| :--- | :---: | :--- | :---: |",
        ]

        layer_nodes = [nodes[nid] for nid in layer.get("nodeIds", []) if nid in nodes]
        files_and_classes = [n for n in layer_nodes if n.get("type") in ("file", "class", "service", "interface")]
        for n in files_and_classes:
            name = n.get("name", n.get("id"))
            ntype = n.get("type", "component")
            summary = n.get("summary", "")
            complexity = n.get("complexity", "moderate")
            lines.append(f"| **`{name}`** | `{ntype}` | {summary} | `{complexity}` |")

        lines.extend([
            "",
            "## Member Functions & Endpoints",
            "",
            "| Symbol | Summary | Tags |",
            "| :--- | :--- | :--- |",
        ])

        funcs = [n for n in layer_nodes if n.get("type") in ("function", "endpoint")]
        for fn in funcs:
            fname = fn.get("name", fn.get("id"))
            fsummary = fn.get("summary", "")
            ftags = ", ".join(f"`{t}`" for t in fn.get("tags", []))
            lines.append(f"| **`{fname}`** | {fsummary} | {ftags} |")

        lines.append("")

        out_file = ARCH_DIR / cfg["filename"]
        out_file.write_text("\n".join(lines), encoding="utf-8")
        print(f"Generated: {out_file}")


def generate_domain_flows(dg: Dict[str, Any]):
    FLOWS_DIR.mkdir(parents=True, exist_ok=True)
    nodes = {n["id"]: n for n in dg.get("nodes", [])}
    domains = [n for n in dg.get("nodes", []) if n.get("type") == "domain"]
    flows = [n for n in dg.get("nodes", []) if n.get("type") == "flow"]
    steps = [n for n in dg.get("nodes", []) if n.get("type") == "step"]

    # Generate Index
    index_lines = [
        "# Business Domain Flows",
        "",
        "Cantus models core platform operations as structured **Domain Flows**. Each flow maps end-to-end business logic, trigger conditions, step sequences, and exact source code locations.",
        "",
        "## Domain Map",
        "",
        "| Domain | Summary | Key Entities | Business Flows |",
        "| :--- | :--- | :--- | :--- |",
    ]

    domain_slug_map = {
        "domain:spotify-authentication-and-session-management": ("Spotify Authentication & Session Management", "spotify-pkce-login.md"),
        "domain:real-time-playback-monitoring": ("Real-Time Playback Monitoring & Polling", "playback-sync.md"),
        "domain:synchronized-lyrics-retrieval-and-caching": ("Synchronized Lyrics Retrieval & Caching", "lyrics-caching.md"),
        "domain:client-clock-synchronization-and-rendering": ("Client Clock Synchronization & Dynamic Rendering", "ntp-interpolation.md"),
    }

    for d in domains:
        dname = d.get("name", "")
        summary = d.get("summary", "")
        entities = ", ".join(f"`{e}`" for e in d.get("domainMeta", {}).get("entities", []))
        _, link = domain_slug_map.get(d["id"], (dname, "index.md"))
        index_lines.append(f"| **[{dname}]({link})** | {summary} | {entities} | [View Flow]({link}) |")

    index_lines.extend([
        "",
        "## End-to-End System Interaction",
        "",
        "```mermaid",
        "sequenceDiagram",
        "    autonumber",
        "    actor User",
        "    participant Client as Uno Platform Client",
        "    participant Hub as SignalR PlaybackHub",
        "    participant Poller as ActiveUsersPlaybackMonitor",
        "    participant Spotify as Spotify Web API",
        "    participant Cache as SQLite Lyrics Cache",
        "    participant LRCLIB as LRCLIB Lyrics API",
        "",
        "    User->>Client: Connect to Cantus Room",
        "    Client->>Hub: Join Room / Sync Clock (NTP ping)",
        "    Hub-->>Client: NTP pong (server timestamps)",
        "    Poller->>Spotify: Poll active playback state",
        "    Spotify-->>Poller: Current track, progress, is_playing",
        "    Poller->>Cache: Query cached lyrics for trackId",
        "    alt Cache Miss",
        "        Cache->>LRCLIB: Query lyrics by title/artist",
        "        LRCLIB-->>Cache: Raw LRC synced text",
        "        Cache->>Cache: Store in SQLite with 30-day expiry",
        "    end",
        "    Poller->>Hub: Broadcast PlaybackState + SyncedLyrics",
        "    Hub->>Client: Send real-time state & parsed lyric lines",
        "    Client->>Client: Interpolate clock position & scroll active lyric",
        "```",
        "",
    ])

    (FLOWS_DIR / "index.md").write_text("\n".join(index_lines), encoding="utf-8")
    print(f"Generated: {FLOWS_DIR / 'index.md'}")

    flow_page_configs = [
        {
            "domain_id": "domain:spotify-authentication-and-session-management",
            "filename": "spotify-pkce-login.md",
            "title": "Spotify PKCE Authentication & Session Management",
            "flow_ids": ["flow:spotify-pkce-login"]
        },
        {
            "domain_id": "domain:real-time-playback-monitoring",
            "filename": "playback-sync.md",
            "title": "Real-Time Playback Monitoring & SignalR Broadcast",
            "flow_ids": ["flow:active-playback-polling"]
        },
        {
            "domain_id": "domain:synchronized-lyrics-retrieval-and-caching",
            "filename": "lyrics-caching.md",
            "title": "Synchronized Lyrics Retrieval & SQLite Caching",
            "flow_ids": ["flow:fetch-and-cache-lyrics", "flow:track-latency-offset-adjustment"]
        },
        {
            "domain_id": "domain:client-clock-synchronization-and-rendering",
            "filename": "ntp-interpolation.md",
            "title": "Client Clock Synchronization & NTP Interpolation",
            "flow_ids": ["flow:ntp-clock-synchronization", "flow:realtime-lyric-rendering"]
        },
    ]

    for cfg in flow_page_configs:
        domain = nodes.get(cfg["domain_id"])
        if not domain:
            continue

        lines = [
            f"# {cfg['title']}",
            "",
            domain.get("summary", ""),
            "",
            "## Domain Rules & Constraints",
            "",
        ]

        for rule in domain.get("domainMeta", {}).get("businessRules", []):
            lines.append(f"- **{rule}**")

        lines.extend([
            "",
            "## Key Domain Entities",
            "",
            "| Entity | Description |",
            "| :--- | :--- |",
        ])

        for ent in domain.get("domainMeta", {}).get("entities", []):
            lines.append(f"| **`{ent}`** | Core domain entity representing state within {domain.get('name')} |")

        for fid in cfg["flow_ids"]:
            flow = nodes.get(fid)
            if not flow:
                continue

            entry_point = flow.get("domainMeta", {}).get("entryPoint", "Internal Method")
            entry_type = flow.get("domainMeta", {}).get("entryType", "internal")

            lines.extend([
                "",
                "---",
                f"## Flow: {flow.get('name')}",
                "",
                flow.get("summary", ""),
                "",
                f"- **Entry Point**: `{entry_point}` ({entry_type})",
                f"- **Complexity**: `{flow.get('complexity', 'moderate')}`",
                "",
                "### Step Sequence & Source Locations",
                "",
                "| Step | Name | Summary | Source Location |",
                "| :---: | :--- | :--- | :--- |",
            ])

            flow_steps = [s for s in steps if s["id"].startswith(f"step:{fid.replace('flow:', '')}:")]
            for idx, s in enumerate(flow_steps, start=1):
                sname = s.get("name", "")
                ssummary = s.get("summary", "")
                fpath = s.get("filePath", "")
                lrange = s.get("lineRange", [1, 1])
                lines.append(f"| {idx} | **{sname}** | {ssummary} | `{fpath}#L{lrange[0]}-L{lrange[1]}` |")

            lines.extend([
                "",
                "### Execution Flowchart",
                "",
                "```mermaid",
                "flowchart TD",
            ])

            for idx, s in enumerate(flow_steps, start=1):
                clean_step_name = sanitize_mermaid_label(s.get("name", f"Step {idx}"))
                node_code = f"S{idx}"
                lines.append(f'    {node_code}["{idx}. {clean_step_name}"]')
                if idx > 1:
                    lines.append(f"    S{idx-1} --> {node_code}")

            lines.extend([
                "```",
                "",
            ])

        out_file = FLOWS_DIR / cfg["filename"]
        out_file.write_text("\n".join(lines), encoding="utf-8")
        print(f"Generated: {out_file}")


def main():
    print("Extracting Understand-Anything graph data...")
    kg = load_json(UA_DIR / "knowledge-graph.json")
    dg = load_json(UA_DIR / "domain-graph.json")

    print("Generating architecture documentation...")
    generate_architecture_overview(kg)
    generate_layer_docs(kg)

    print("Generating domain flows documentation...")
    generate_domain_flows(dg)

    print("Deterministic documentation generation completed successfully.")


if __name__ == "__main__":
    main()
