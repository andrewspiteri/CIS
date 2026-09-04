---
name: cis-frontend-context
description: Discover and validate framework-neutral frontend screens, routes, components, navigation, state, and API calls. Use before frontend impact analysis, design reconciliation, graph builds, or broad source searches in web, native, or Godot clients.
---

# CIS frontend context

1. Read the repository profile, screen-route map, and UI framework profile when present.
2. Run `cis frontend discover --format agent`.
3. Run `cis frontend validate --strict --format agent`.
4. Use filtered inventory for bounded routing.
5. Rebuild the graph after valid discovery.
6. Inspect source before semantic claims; adapters are deterministic routing evidence, not compiler proof.

Built-in adapters cover React/Next.js, Angular, Vue, SwiftUI, Jetpack Compose, and Godot. Duplicate provider names fail closed.
