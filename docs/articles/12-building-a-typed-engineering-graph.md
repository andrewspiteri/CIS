---
title: "Building a Typed Engineering Graph"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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
- catalogued document, addressable document section, requirement, and decision;
- governed reference items with API, command, event, permission, package, problem,
  route, ownership, configuration, traceability, and data facets;
- source file, symbol, compiler-bound invocation, and bounded dataflow;
- test and workflow; and
- external or repository dependency.

Types let queries remain precise. “Find documents related to this API” is different from
“find source symbols that call this implementation.” A generic blob graph would force
every consumer to rediscover those semantics.

Typed identity also prevents attractive but invalid shortcuts. A document section that
describes a route is not automatically the API operation itself. A test file is not the
same node as an individual test. A repository dependency is not necessarily a product
implementation target. Keeping those concepts separate lets later relationships say
exactly what the evidence supports.

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

## The build is an evidence pipeline

A useful graph build can be read as a sequence:

```text
Validate canonical inputs
  → classify repositories and components
  → extract documents and governed rows
  → inspect source, symbols, tests, and workflows
  → normalize typed nodes and evidence-backed edges
  → validate the candidate generation
  → commit the local generation atomically
```

Each stage should be able to explain what it accepted, rejected, skipped, or bounded.
When a parser cannot understand a file, the build records a diagnostic instead of
inventing a partial relationship. When a provider returns an unknown kind, normalization
fails closed rather than allowing a new vocabulary to leak into downstream queries.

## Build from deterministic evidence first

The graph build validates catalog inputs, reads governed documents and references,
classifies components, extracts source and test evidence, and normalizes relationships.
Compiler-backed extraction is preferred where supported. Bounded textual discovery fills
specific gaps but retains its weaker method and confidence.

Registered context providers can add normalized framework evidence without changing the
graph's authority model. Frontend providers, for example, may contribute route, component,
state, data-access, and test relationships with provider/version provenance. Provider
output is still validated and stored as derived graph evidence.

Generated dependencies, build output, vendor trees, `.git`, and `.cis/local/` are pruned
before source discovery. Sensitive values are excluded from node properties.

Deterministic extraction comes first because it is repeatable and inspectable. That does
not make it universally complete. Compiler analysis can establish a particular call in a
supported project, while a reviewed declaration may be the strongest evidence for a
dynamic integration. Model assistance is valuable for routing and proposals when its
weaker authority remains visible.

## Make rebuilds transactional and incremental

CIS stores the graph in versioned local SQLite. Content hashes let unchanged inputs be
reused. A build writes a complete generation transactionally so a failure cannot leave
half of the repository on a new schema and half on the old one.

A manifest records inputs, extractor versions, repository identity, and build identity.
Validation can then distinguish structural corruption from simple staleness.

Incremental does not mean mixing generations. Unchanged inputs may reuse prior extraction
results, but readers should observe one coherent build identity. If the final write fails,
the earlier valid generation remains available. This matters in multi-repository work,
where a half-updated graph could route a task to a repository that was never successfully
analysed.

## Query without loading everything

Indexed queries support filters by kind, text, state, confidence, and repository. Related
traversal is cycle-safe and bounded. Shortest-path tracing retains direction, evidence,
relationship state, and depth. Export can produce Markdown, JSON, JSONL, or Graphviz DOT
without making an exported file authoritative.

The query contract is deliberately read-only. Durable corrections happen in canonical
sources or reviewed relationship declarations, followed by a rebuild.

## Follow one relationship chain

Suppose a reviewer starts from a requirement to expose a featured catalogue. A useful
path might be:

```text
Requirement
  → specifies API operation
  → owned by catalogue component
  → implemented by handler symbol
  → calls query abstraction
  → verified by integration test
  → packaged by delivery workflow
```

Every arrow answers a different question. The path should show repository, direction,
state, confidence, and evidence. If the requirement-to-operation edge is proposed while
the handler call is compiler-observed, the output must preserve that difference. Impact
analysis can then request review of the weak link instead of presenting the entire chain
as equally certain.

## Correct sources, not graph rows

When a query exposes a wrong owner, the durable fix belongs in the ownership map or other
canonical declaration. When a source extractor misses a framework pattern, the fix
belongs in the provider and its regression tests. Editing SQLite directly would create a
local fact that disappears on rebuild and cannot be reviewed by the team.

This discipline makes the graph safe to discard and powerful to improve. Better
extractors increase navigation quality without transferring product authority into the
database.

## Keep validation independent

Graph validation checks schema compatibility, identities, endpoints, evidence, allowed
types, freshness, path containment, sensitive properties, and accidental Git tracking.
It reconstructs a portable graph shape from SQLite when full structural inspection is
needed.

A valid graph is internally consistent. It is not a proof that the repository contains
every relevant relationship.

Useful graph health therefore has at least three dimensions: structural validity,
freshness against current inputs, and declared coverage limits. Reporting only “valid”
encourages consumers to infer more than the validator established.

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
- [Frontend context providers](../specs/frontend-context-provider-spec.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)
