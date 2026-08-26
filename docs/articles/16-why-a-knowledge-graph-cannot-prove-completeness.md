---
title: "Why a Knowledge Graph Cannot Prove Completeness"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on graph or impact-completeness change
summary: "A graph can prove what it traversed and validated, but not that every semantic obligation exists in its model."
cis:
  stable_id: change-impact-studio:article:graph-cannot-prove-completeness
---

# Why a knowledge graph cannot prove completeness

A well-built engineering graph can reveal relationships that a developer would otherwise
miss. That power creates a dangerous temptation: treat a successful traversal as proof
that every impact has been found.

No graph can support that claim unless the world it models is already known to be complete.

## Some knowledge is absent

Material obligations may exist outside the repository:

- an undocumented customer promise;
- production behavior not represented by tests;
- a vendor constraint;
- a regulatory interpretation;
- an operational workaround;
- a planned but uncommitted migration; or
- knowledge held by a specialist.

The graph cannot traverse a node that does not exist.

## Some relationships are ambiguous

Textual similarity, naming, and model interpretation can propose relationships, but
proposals contain false positives and false negatives. Even compiler evidence has a
boundary: dynamic dispatch, reflection, configuration, external calls, and generated
code can hide runtime relationships.

Confidence and extraction method help describe this uncertainty; they do not eliminate it.

## Bounds are necessary

Unbounded graph traversal can become unusable. CIS sets explicit roots, relationship
states, directions, depth, confidence, and path-count limits. Cycles are handled safely.

When a bound truncates the result, truncation remains visible. The user can broaden the
search and rerun it. The tool does not relabel a partial traversal as complete.

## What completeness can honestly mean

CIS impact completeness means that, within the declared baseline and bounds:

- findings were discovered;
- every proposed finding received a disposition;
- accepted findings are known scope;
- deferred findings are not hidden; and
- traversal was not truncated.

This is workflow completeness, not semantic omniscience.

## Validation and completeness are different

Graph validation can prove that identities are unique, endpoints exist, evidence is
well formed, paths are contained, inputs are fresh, and storage is structurally sound.
It cannot prove that the model includes every real dependency.

A green validator means the represented graph is valid. It does not mean reality has no
unrepresented edge.

## Human review remains necessary

Reviewers contribute knowledge the graph does not have. They can add roots, declare
relationships, reject misleading proposals, and identify external obligations. Surprises
found during implementation and verification should feed reviewed improvements to future
graph builds.

The system becomes more useful over time without ever claiming perfect coverage.

## Takeaway

Use an engineering graph to make evidence and relationships navigable. Ask it to show
its baseline, bounds, confidence, provenance, diagnostics, and truncation.

Do not ask it to prove that no unknown impact exists. Honest bounded completeness is a
stronger control than an impossible universal claim.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis impact completeness`](../manual/cis_impact_completeness.md)
- [`cis graph validate`](../manual/cis_graph_validate.md)

