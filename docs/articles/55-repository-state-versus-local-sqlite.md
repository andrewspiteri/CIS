---
title: "Repository-Backed State Versus Local SQLite"
type: article
status: Active
series: "Building Change Impact Studio"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

Read scopes, file-content hashes, graph-validation caches, and indexed metadata can avoid
repeating unchanged work. These optimizations must invalidate against exact inputs and
leave the last healthy graph usable after a failed replacement.

## Content hashes connect the layers

Graph records cite canonical IDs, paths, hashes, and extractor versions. Validation can
detect stale input and rebuild safely.

## Disposal is a design test

Removing SQLite may discard query performance and cached extraction. It must not discard
a decision or approval.

## Choose storage through two questions

Ask whether the value carries durable authority and how it needs to be queried.

| Need | Appropriate home |
|---|---|
| Human-reviewed intent, decision, task, exception, or acceptance | Versioned repository file |
| Stable product or workspace identity | Explicit repository configuration |
| Fast traversal and indexed relationship filtering | Local SQLite projection |
| Large run logs and recoverable derived artifacts | Bounded local artifact storage |
| Remote tracker status | External projection plus canonical link state |

High query volume does not justify moving authority into SQLite. Instead, project the
canonical source and retain its identity and digest.

## Build coherent graph generations

The graph database contains normalized nodes, edges, evidence, input manifests, and build
metadata. Incremental extraction can reuse unchanged inputs, but readers should see one
complete generation. A failed transaction leaves the last healthy graph available rather
than exposing a half-updated mix.

Schema versioning and validation distinguish incompatible storage, corrupt state, and
ordinary staleness. Rebuilding is a supported recovery path because the canonical inputs
remain elsewhere.

## Treat freshness as a query precondition

A graph can be structurally valid and stale against current source. Content hashes,
extractor versions, repository identity, and graph manifests let commands detect that
condition. Depending on the operation, CIS can require rebuild, warn, or expose the stale
boundary for review.

SQLite never decides that a changed source remains semantically equivalent. It reports
the mismatch; canonical authority and workflow rules decide the consequence.

## Control concurrent access

Read scopes should observe a consistent generation while builds prepare a replacement.
Writers use bounded transactions and atomic replacement so an editor refresh, agent
query, and graph build cannot corrupt each other's view. Caches tied to file hashes and
generation identity invalidate when another controller changes the data.

## Retention does not change authority

Local databases and related artifacts may be inventoried, archived, compacted, restored,
or removed under retention policy. Hashes and manifests protect integrity. Restoration
recovers query or diagnostic capability, not a lost product decision.

## Use the deletion test

Stop the tool and imagine `.cis/local/` has been removed. Rebuilding may cost time, and
some disposable run detail may be gone. Requirements, ownership, approved scope,
exceptions, and acceptance must remain intact in Git. If they do not, the storage boundary
has leaked.

## Avoid backup confusion

Backing up `.cis/local/` can preserve expensive diagnostics and speed recovery, but it is
not a substitute for versioning canonical repository files. Restoring an old graph over a
new checkout should surface staleness immediately. Restoring a canonical decision requires
normal Git and governance history, not a database archive.

## Review a new store

Before adding local persistence, document its purpose, authority, schema version,
containment, sensitivity, invalidation inputs, concurrency model, size bounds, retention,
rebuild path, and failure behavior. If no reliable rebuild or promotion boundary exists,
the design is likely mixing durable meaning with operational convenience.

## Takeaway

Choose storage by authority and access pattern. Keep meaning in reviewable repository
state and use local SQLite as a rebuildable indexed projection.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [Documentation inventory and validation](../specs/documentation-inventory-and-validation-spec.md)
- [Local artifact retention and recovery](../specs/local-artifact-retention-spec.md)
