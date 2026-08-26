---
title: "Stale-Safe Task Envelopes"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on agent-envelope change
summary: "How path and digest binding prevents old prepared work from being accepted against a changed task."
cis:
  stable_id: change-impact-studio:article:stale-safe-task-envelopes
---

# Stale-safe task envelopes

Prepared agent work can outlive the task that created it. A decision changes, a
specification is revised, or targets are narrowed while an executor continues from an
older package.

Without stale detection, both versions look legitimate.

## Bind the envelope to canonical identity

CIS task envelopes record the change ID, work ID, canonical task path, task digest,
repository identity, and preparation metadata. The digest represents the exact task
contract supplied to the executor.

## Validate again on result ingestion

When a result returns, CIS checks envelope identity and compares the recorded digest
with the current task. It validates structured fields and ensures changed paths are
repository relative.

A mismatch is evidence that the work targeted different authority. The system stops
instead of merging the result optimistically.

## Stale does not mean useless

The implementation may contain valuable work. A human can inspect it, revise the plan,
or prepare a new task. The result cannot be accepted automatically as fulfillment of
the current task.

## Import is not completion

Valid ingestion appends evidence. It does not transition the task, approve scope, or
accept verification. Those gates use current canonical state and independent evidence.

## Takeaway

Portable execution needs freshness as well as identity. Bind every envelope to the
exact task digest, revalidate it when results return, and treat stale work as a review
case rather than current completion evidence.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [`cis agent import-result`](../manual/cis_agent_import_result.md)
