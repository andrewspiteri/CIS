---
title: "Context Should Be Routed, Not Dumped"
type: article
status: Active
series: "Governed Software Change"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
review_cadence: on product or context-model change
summary: "How provenance, typed relationships, and bounded retrieval provide better engineering context than loading an entire repository."
cis:
  stable_id: change-impact-studio:article:context-should-be-routed-not-dumped
---

# Context should be routed, not dumped

When a coding agent lacks context, the obvious response is to give it more. Add the
architecture document. Include the repository map. Paste the API specification. Attach
the test output. Let it search every file.

That approach eventually fails. Large context can contain more facts while making the
important relationships harder to see. It can mix current authority with obsolete
notes, implementation evidence with product intent, and deterministic facts with model
interpretation. It also increases cost, latency, privacy exposure, and the chance that
the executor will follow the wrong signal.

The goal is not maximum context. It is the smallest trustworthy context that supports
the current decision.

## Repository knowledge has several forms

An engineering repository contains more than source code. Relevant knowledge may live in:

- product and technical specifications;
- architecture decisions;
- standards and approved exceptions;
- API, event, command, permission, and configuration inventories;
- source symbols and dependencies;
- tests and workflow definitions;
- package metadata;
- change dossiers and verification evidence; and
- Git baselines and history.

These sources carry different authority. A source declaration proves that a symbol
exists. It does not prove that the symbol matches current product intent. A test proves
the behavior it actually asserts, not every behavior suggested by its name. A proposed
graph relationship is not equivalent to a reviewed declaration.

Useful context preserves those distinctions.

## Start with routing metadata

Change Impact Studio builds incremental file index cards as local routing state. A card
can identify a file's likely purpose, concepts, symbols, sensitivity, and content hash
without making the card authoritative.

That creates a two-stage workflow:

1. use compact routing metadata to identify likely sources;
2. open the canonical or implementation source before making a factual claim.

This is more efficient than repeatedly scanning the whole repository and safer than
trusting a generated summary. If the source content changes, the hash makes the card
stale. If the card is deleted, it can be rebuilt.

Routing state narrows attention; it does not replace evidence.

## Typed graphs make relationships queryable

A flat search finds matching text. Change analysis often needs relationships:

- Which component owns this API operation?
- Which tests verify this symbol?
- Which feature depends on this contract?
- Which workflow packages the affected project?
- Which standard applies to this public endpoint?
- Which other repository consumes the interface?

CIS projects repository knowledge into a typed local graph. Nodes represent documents,
components, contracts, source files, symbols, tests, workflows, dependencies, and
governed reference items. Edges identify relationships such as ownership, implementation,
verification, calls, dependency, generation, or provenance.

Every useful edge needs more than two endpoints. It also needs evidence, extraction
method, confidence, and relationship state. A compiler-observed call is different from
a model-proposed relationship. A human-confirmed dependency is different from a naming
similarity.

The graph records those differences so downstream tools do not flatten uncertainty
into fact.

## The graph is derived, not sovereign

Graph databases are attractive sources of truth because they are easy to query. That
is also the danger. If a durable decision only exists as a graph property, rebuilding
the graph can change the product's meaning.

In CIS, canonical facts remain in repository files. The graph projects those facts and
implementation evidence into disposable SQLite state. A node marked canonical points
to a canonical source; the graph row itself is still derived.

This boundary provides three benefits:

- Git remains the review and history mechanism for durable meaning;
- graph extraction can improve without silently rewriting authority; and
- stale or corrupt local state can be discarded and rebuilt.

The graph is a routing and reasoning aid. It is not proof that every semantic impact
has been found.

## Bounded traversal matters

Relationship traversal can expand quickly. A public contract may connect to consumers,
implementations, tests, packages, workflows, documentation, and transitive dependencies.
Following every edge can produce a context pack as indiscriminate as loading the whole
repository.

CIS therefore uses bounded, cycle-safe traversal with explicit roots, depth, relationship
states, confidence, and path limits. Truncation remains visible. It is not converted
into a false completeness claim.

A user can broaden the roots or bounds and rerun the analysis. The important property
is that the system reports the boundary it actually explored.

## Context packs should answer one task

A context pack is most useful when it is assembled for a specific question:

- explain this contract;
- find callers of this symbol;
- identify tests for this component;
- prepare one bounded work item; or
- trace a requirement to implementation and verification.

The pack should include exact source paths, relevant excerpts, identities, evidence,
and freshness. It should exclude unrelated files, secrets, generated dependencies, and
broad narrative that does not help the task.

This produces a better handoff to a human or agent. The executor can see why each source
was selected and can open the original file when more detail is needed.

## Privacy is part of context quality

Context routing is also a privacy control. A local model or deterministic command may
be appropriate for repository classification, while a remote model may not be
authorized to receive the same content.

CIS treats automatic provider selection as local-only. Remote content transmission
requires a governed route and explicit authorization. Likely secrets, keys, certificates,
credentials, and sources marked sensitive are excluded rather than redacted optimistically
and transmitted.

The best context is not only relevant. It is permitted.

## Worked contrast: prepare context for one invitation task

The following context lists are hypothetical and deliberately omit repository-specific
paths. They illustrate selection, not literal CIS context-pack output.

The task is narrow: implement the approved API behavior that revokes an outstanding
Friends Todo invitation.

### Without routing

An executor receives all three product repositories, the complete product handbook,
historic design notes, every API operation, full CI logs, and prior agent conversations.
Some documents describe abandoned sharing behavior. Several tests mention “access” but
exercise unrelated list membership. The infrastructure repository contains settings
and files the task is not authorized to transmit to a remote provider.

The executor has more text but weaker signals. It must infer which specification is
current, which relationship is real, and which material is permitted. A plausible
answer can follow an obsolete note or expand into customer and infrastructure work that
belongs to other tasks.

### With routed context

The task receives a bounded set selected for a stated reason:

| Selected evidence | Why it is present |
|---|---|
| Current feature specification and digest | Establishes the approved revocation behavior and non-goals |
| Resolved link-lifecycle decision | Explains the authorized token and revocation semantics |
| API contract section | Defines the operation and problem responses this task must preserve |
| Authorization policy and owning application service | Shows the relevant trust and ownership boundary |
| Focused lifecycle and contract tests | Shows existing evidence and where changed behavior must be verified |

Each item retains its source, identity, freshness, and authority. The graph path and
traversal bounds remain visible. Unrelated UI files, broad logs, generated dependencies,
likely secrets, and unapproved remote content are excluded. If implementation reveals
that revocation also requires infrastructure work, the executor reports the missing
relationship and requests scope review instead of searching and changing the rest of
the workspace silently.

The governed pack is smaller, but its limits are explicit. That makes it more useful
than a larger collection whose authority and permissions the executor must guess.

## Takeaway

Context quality comes from identity, authority, provenance, freshness, and relevance—not
from volume.

Index to route. Open the source to establish facts. Use typed relationships to navigate
impact. Bound every traversal and expose truncation. Keep generated context disposable.
Give each task the evidence it needs and nothing it is not authorized to receive.

Context should be routed, not dumped.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [File index cards](../specs/file-index-card-spec.md)
- [System context](../specs/system-context-spec.md)
- [AI routing profile](../references/ai-routing-profile.md)
