---
title: "Repository-Backed State Versus Local SQLite"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on storage architecture change
summary: "Markdown and explicit configuration own meaning; normalized SQLite makes derived graph queries efficient."
cis:
  stable_id: change-impact-studio:article:repository-state-versus-sqlite
---

# Repository-backed state versus local SQLite

Markdown is excellent for review and poor for repeated graph traversal. SQLite is
excellent for indexed queries and poor as an invisible authority store. CIS uses both.

## Repository files own durable meaning

Intent, standards, decisions, plans, and acceptance remain diffable and versioned.
Structured YAML records repository identity, workspace membership, and catalog routing.

## SQLite owns derived query performance

The graph module normalizes nodes, edges, evidence, and manifests into versioned local
SQLite. Queries can filter and traverse without loading a whole JSON graph.

## Content hashes connect the layers

Graph records cite canonical IDs, paths, hashes, and extractor versions. Validation can
detect stale input and rebuild safely.

## Disposal is a design test

Removing SQLite may discard query performance and cached extraction. It must not discard
a decision or approval.

## Takeaway

Choose storage by authority and access pattern. Keep meaning in reviewable repository
state and use local SQLite as a rebuildable indexed projection.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [Documentation inventory and validation](../specs/documentation-inventory-and-validation-spec.md)
