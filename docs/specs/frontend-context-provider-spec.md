---
title: "Frontend Context Providers"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on frontend framework, provider contract, route, graph, or classification change"
cis:
  stable_id: change-impact-studio:spec:frontend-context-providers
---

# Frontend context providers

## Contract

`ICisFrontendContextProvider` accepts a repository-scoped source file and emits normalized observations without changing source or canonical documentation. Every observation identifies provider, framework, kind, stable ID, display name, source path/line, optional route and target, and bounded properties.

Built-in providers cover:

- React and Next.js app/pages routing;
- Angular components, routes, and HTTP clients;
- Vue components, routes, and HTTP clients;
- SwiftUI views, navigation, and URLSession use;
- Jetpack Compose composables, routes, and navigation;
- Godot scenes, scene changes, and HTTPRequest use.

Extensions may register providers through the public contract. Duplicate provider names fail closed and are never resolved by assembly order.

## Normalized state and validation

`cis frontend discover` excludes dependency, build, generated, coverage, Git, and `.cis/local/` paths; bounds source files to 2 MB; and writes idempotent state to `.cis/local/frontend/context.json`. The digest covers normalized observations and diagnostics, not the creation timestamp.

Validation detects provider conflicts, source failures, route collisions, and observed routes missing from a populated canonical `screen-route-map.md`. Strict mode promotes drift warnings to failure. The canonical map retains authority for screen ownership, classification, access, and destination meaning.

## Graph integration

`FrontendContextService` implements the public graph-augmenter contract. Graph builds include its version in build identity and add derived screen, route, UI-component, navigation, state-store, and API-client nodes with source provenance. Repository containment, route rendering, and resolvable navigation relationships are added deterministically.

Frontend observations are routing evidence. They do not prove runtime behavior, authorization, accessibility, design approval, or complete framework semantics; agents must inspect source and applicable canonical records.
