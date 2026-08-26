---
title: "Building a Typed Engineering Graph"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on graph-model change
summary: "How CIS turns documents, contracts, source, tests, and workflows into an evidence-backed local graph without replacing their sources."
cis:
  stable_id: change-impact-studio:article:building-typed-engineering-graph
---

# Building a typed engineering graph

A text search can find where a term appears. Change impact needs to know how engineering
concerns relate.

Which component owns an operation? Which source implements a contract? Which tests verify
it? Which workflow packages the affected project? Which feature or decision established
the behavior? These are graph questions.

## Begin with stable identity

A graph is only as reliable as its node identity. CIS gives precedence to existing
stable IDs from the documentation catalog and governed reference rows. Components use
repository-qualified identities. Source files use normalized repository-relative paths.
Symbols combine component and compiler identity where available.

Stable identity makes rebuilds idempotent. The same contract should remain the same node
even when unrelated graph content changes.

## Use types that carry engineering meaning

CIS represents several node families:

- repository and component;
- catalogued document and governed reference item;
- API, command, event, permission, package, problem, route, and data contract;
- source file and symbol;
- test and workflow job; and
- change, decision, task, and evidence records.

Types let queries remain precise. “Find documents related to this API” is different from
“find source symbols that call this implementation.” A generic blob graph would force
every consumer to rediscover those semantics.

## Edges need contracts too

Relationships include ownership, implementation, dependency, calls, verification,
generation, provenance, and change impact. Each edge records:

- source and target;
- relationship type;
- state;
- confidence;
- evidence location;
- extraction method; and
- relevant content hash or build identity.

An observed compiler call and a model-proposed dependency can coexist without being
treated as equivalent facts.

## Build from deterministic evidence first

The graph build validates catalog inputs, reads governed documents and references,
classifies components, extracts source and test evidence, and normalizes relationships.
Compiler-backed extraction is preferred where supported. Bounded textual discovery fills
specific gaps but retains its weaker method and confidence.

Generated dependencies, build output, vendor trees, `.git`, and `.cis/local/` are pruned
before source discovery. Sensitive values are excluded from node properties.

## Make rebuilds transactional and incremental

CIS stores the graph in versioned local SQLite. Content hashes let unchanged inputs be
reused. A build writes a complete generation transactionally so a failure cannot leave
half of the repository on a new schema and half on the old one.

A manifest records inputs, extractor versions, repository identity, and build identity.
Validation can then distinguish structural corruption from simple staleness.

## Query without loading everything

Indexed queries support filters by kind, text, state, confidence, and repository. Related
traversal is cycle-safe and bounded. Shortest-path tracing retains direction, evidence,
relationship state, and depth. Export can produce Markdown, JSON, JSONL, or Graphviz DOT
without making an exported file authoritative.

The query contract is deliberately read-only. Durable corrections happen in canonical
sources or reviewed relationship declarations, followed by a rebuild.

## Keep validation independent

Graph validation checks schema compatibility, identities, endpoints, evidence, allowed
types, freshness, path containment, sensitive properties, and accidental Git tracking.
It reconstructs a portable graph shape from SQLite when full structural inspection is
needed.

A valid graph is internally consistent. It is not a proof that the repository contains
every relevant relationship.

## Takeaway

A useful engineering graph needs stable identity, meaningful types, evidence-backed
relationships, incremental builds, bounded queries, and an explicit source-of-truth
boundary.

Build the graph to navigate repository knowledge—not to replace it.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Module ownership map](../references/module-ownership-map.md)
- [Documentation inventory and validation](../specs/documentation-inventory-and-validation-spec.md)
- [Graph command manual](../manual/cis_graph_build.md)

