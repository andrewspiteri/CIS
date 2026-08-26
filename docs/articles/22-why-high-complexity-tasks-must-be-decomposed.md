---
title: "Why High-Complexity Tasks Must Be Decomposed"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on task-planning change
summary: "Complexity should constrain execution by creating bounded child tasks, not merely label a large instruction."
cis:
  stable_id: change-impact-studio:article:decompose-high-complexity-tasks
---

# Why high-complexity tasks must be decomposed

A task labelled “high complexity” remains dangerous if it is still handed to one
executor as a single instruction.

The label acknowledges risk without changing the execution boundary.

## Complexity combines several dimensions

Work becomes complex through more than estimated effort. Signals include multiple
repositories, public contracts, security, data migration, cross-surface behavior,
provider handoffs, operational rollout, and unresolved dependencies.

These signals increase the number of decisions and failure modes an executor must hold
at once.

## Parents express outcomes; children express execution

CIS does not allow a high-complexity work item to remain directly executable. It becomes
a decomposed parent with at least two low- or medium-complexity children.

The parent retains:

- the outcome;
- accepted impact and requirement coverage;
- shared constraints; and
- completion relationship.

Children receive bounded objectives, targets, dependencies, validation, and evidence.

## Decomposition should follow risk boundaries

Useful boundaries include contracts before consumers, schema before backfill, wireframes
before visual design, design before implementation, and implementation before independent
verification.

Splitting one broad task into arbitrary file groups does not reduce conceptual risk.
Each child should be independently understandable and verifiable.

## Coverage must survive decomposition

Requirements and accepted impacts cannot disappear when work is split. Parent and child
records preserve traceability so validation can show which executable task covers each
obligation.

## Takeaway

Complexity classification should change what may execute. Use a high-complexity parent
to preserve the outcome, then create bounded children around contracts, risk, ownership,
and validation boundaries.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Core task-type catalogue](../specs/core-task-type-catalog.md)

