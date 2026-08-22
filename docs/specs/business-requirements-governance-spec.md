---
title: "Business Requirements Governance"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on BRD workflow change"
cis:
  stable_id: change-impact-studio:spec:business-requirements-governance
---

# Business requirements governance

## Purpose

CIS establishes one canonical business requirements document for a multi-repository
product while preserving discovered BRDs, domain-equivalent product-design documents
such as game design documents (GDDs), and development feature specifications as
evidence. Discovery never proves currency or semantic absorption.

## Authority model

`cis workspace init` registers exactly one documentation repository with role
`authority`. Imported product repositories have role `participant`. The canonical BRD
lives at `<authority-documentation-root>/specs/business-requirements.md`; BRDs elsewhere
remain candidate evidence even when they are detailed or appear current.

Legacy registries without roles remain readable and treat entries as participants.
BRD commands require an explicit authority.

## Intake states

When source BRDs exist, discovery records stable candidate identity, repository, path,
content hash, and detection signals. Every source starts `Unreviewed`. When no source
exists, initialization creates the same canonical structure and explicitly records the
absence. Both paths begin `Review Required`.

Domain-specific product authority can be BRD evidence without using BRD terminology.
The deterministic intake recognizes `type: product-design`, GDD filenames, and Game
Design Document headings. These remain assessed source evidence; CIS does not silently
rename or replace the originating product-design document.

Humans classify each source as `Adopted`, `Reference`, or `Rejected`, provide rationale,
and author business outcomes, scope, actors, capabilities and processes, functional and
quality requirements, constraints, success measures, traceability, and open questions.
CIS does not generate stakeholder decisions from implementation evidence.

## Currency and baselines

The canonical BRD records participant graph builds. The authority graph is deliberately
excluded from the embedded baseline because the BRD is itself an authority-graph input;
including it would create a self-referential build identity. Candidate documents in the
authority repository remain content-hashed source evidence.

Approval captures the current participant baselines and a normalized digest of the
approved canonical content excluding approval metadata. Later canonical content edits,
participant build changes, candidate changes, missing evidence, or validation gaps make
an approved BRD effectively `Stale`. Deterministic checks may detect staleness but may
never restore `Active`.

## Feature-specification feedback

Feature specifications created during delivery are downstream design evidence that can
reveal refined, changed, or previously missing business intent. A copied feature template
declares `type: feature-specification`. After its repository graph is rebuilt, discovery
assigns the document a stable source ID and content hash.

`cis brd status` makes an Active BRD Stale when that evidence is unresolved.
`cis brd reconcile` adds new or changed feature specifications to the managed source
assessment table, preserves human-authored sections, resets affected assessments to
`Unreviewed`, and clears prior approval. It never performs an unreviewed semantic merge.

Humans may classify a feature specification as `Adopted`, `Reference`, or `Rejected`.
`Adopted` means its relevant business intent has been incorporated into the canonical
BRD. Validation therefore requires the feature specification's managed `BRD-SRC-*` ID
in the Traceability section before the BRD can be approved again.

## Human authority

Only `cis brd approve` can record `Active`, and it requires human reviewer identity and
rationale. Agents may discover, initialize, reconcile, edit proposed content under
human direction, and validate. They must not assess evidence or approve autonomously.

## Safety and idempotence

Reconciliation preserves human sections and unchanged source assessments. New or
changed BRD or feature-specification evidence resets only affected assessments and clears approval. Managed IDs,
hashes, baseline rows, and block markers remain deterministic. An unmanaged canonical
path or catalog identity is a collision and is never overwritten.
