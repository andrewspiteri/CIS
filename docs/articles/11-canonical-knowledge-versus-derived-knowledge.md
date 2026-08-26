---
title: "Canonical Knowledge Versus Derived Knowledge"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on authority or storage change
summary: "A practical boundary between durable repository authority and rebuildable local routing, analysis, and execution state."
cis:
  stable_id: change-impact-studio:article:canonical-versus-derived-knowledge
---

# Canonical knowledge versus derived knowledge

Engineering tools generate useful state: indexes, graph databases, summaries, caches,
workflow runs, task envelopes, diagnostics, and model analysis. The difficult design
question is not whether to generate them. It is whether any of them are allowed to carry
durable meaning.

Change Impact Studio uses a strict boundary: canonical knowledge is reviewable repository
state; derived knowledge is disposable local state that can be rebuilt from evidence.

## What belongs in canonical state

Canonical records include facts and decisions that must survive a local cache deletion,
tool upgrade, or provider change:

- product and technical intent;
- standards and approved exceptions;
- architecture decisions;
- governed contract inventories;
- approved change scope and resolved decisions;
- task identity, dependencies, and acceptance criteria;
- design and verification approvals; and
- reviewed learning history.

These records belong in version-controlled Markdown or explicit structured repository
files. Humans can inspect the diff, review the lifecycle, and trace the authority.

## What belongs in derived state

Derived state exists to improve routing, performance, or execution:

- SQLite graph generations;
- per-file routing cards;
- model response caches;
- sanitized usage ledgers;
- workflow checkpoints and bounded logs;
- portable agent envelopes;
- imported result snapshots;
- diagnostic analysis; and
- unreviewed learning proposals.

CIS stores this material beneath `.cis/local/` where practical and excludes it from Git.
Deleting it can make the next operation slower, but it cannot erase a requirement,
decision, approval, or acceptance record.

## Why the distinction matters

When derived state becomes authoritative, several failure modes appear:

- rebuilding an index changes the apparent product meaning;
- a local database becomes more current than reviewed documentation;
- one developer's cache contains a decision nobody else can inspect;
- a model summary replaces the source it summarized;
- an external tracker closure becomes the only evidence of task completion; or
- changing provider invalidates the historical record.

The canonical/derived boundary makes these situations structurally incorrect rather
than merely discouraged.

## A canonical projection is still derived

A graph node may have authority `canonical` because it represents a catalogued
specification. The node is not itself the authority. It points to the source file and
retains its stable ID, path, content hash, and provenance.

This distinction allows graph extraction to improve without rewriting product history.
The graph can add a new relationship, change a schema, or rebuild storage while the
canonical document remains unchanged.

## Promotion requires review

Useful derived knowledge sometimes deserves to become canonical. A diagnostic pattern
may reveal a missing standard. A repeated impact surprise may justify a new relationship.
A resolved change decision may have durable architectural value.

Promotion is explicit:

1. preserve the source evidence and digest;
2. formulate the proposed canonical change;
3. identify the owning authority;
4. review conflicts and scope;
5. apply the change through ordinary repository governance; and
6. record the reviewer and rationale.

Copying model output directly into an Active document skips the step where meaning is
confirmed.

## A useful deletion test

Ask of every generated store:

> If this directory disappeared now, what durable meaning would be lost?

If the answer includes a decision, requirement, approval, exception, or acceptance,
the boundary is wrong. Move that meaning into a reviewable canonical record and let the
generated store point to it.

## Takeaway

Canonical state owns durable meaning. Derived state makes that meaning easier to route,
analyse, execute, and verify.

Use content hashes and provenance to connect the two, but never allow convenience stores
to become invisible authorities.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
- [Documentation governance standard](../standards/documentation-governance-standard.md)

