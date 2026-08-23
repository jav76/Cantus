---
trigger: always_on
---

# Understand-Anything & Knowledge Graph Rules

- **Prioritize Knowledge Graph & Domain Context**: Before performing manual codebase research, multiple file scans, or answering natural language questions about system architecture, components, or business processes, always check if `.ua/knowledge-graph.json` or `.ua/domain-graph.json` exists in the workspace root.
- **Architectural Layers & Dependencies**: Use `.ua/knowledge-graph.json` to inspect layers (`client-ui`, `server-api`, `core-domain`, `infrastructure-persistence`, `devops-config`), node types, complexity ratings, and relationships (`calls`, `imports`, `implements`, `contains`, `triggers`, `deploys`).
- **Business Domain & Process Flow Tracing**: Use `.ua/domain-graph.json` to map end-to-end domain logic and step sequences (Spotify PKCE authentication, adaptive playback monitoring, lyrics lookup/caching, NTP clock skew synchronization, and real-time lyric scrolling) directly to their implementing source files and line ranges.
- **Maintain Graph Freshness**: When modifying architectural boundaries, domain contracts, or adding new subsystems, suggest or execute incremental graph updates to keep `.ua/knowledge-graph.json` and `.ua/domain-graph.json` synchronized.
