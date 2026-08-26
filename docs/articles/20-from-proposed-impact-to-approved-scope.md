---
title: "From Proposed Impact to Approved Scope"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on impact-disposition change
summary: "How stable evidence-backed findings move through human acceptance, rejection, or deferral before planning."
cis:
  stable_id: change-impact-studio:article:proposed-impact-approved-scope
---

# From proposed impact to approved scope

Impact analysis is useful because it finds concerns a request did not mention. It is
dangerous when every result automatically becomes planned work.

CIS separates discovery from disposition.

## What a finding contains

Each finding has a stable ID derived from the affected graph target. It records category,
target identity, evidence, rationale, confidence, baseline, and review state.

Stable identity lets re-analysis update evidence without duplicating the concern or
discarding earlier human rationale.

## Four review states

- **Proposed:** the analyser found a candidate; it is not approved scope.
- **Accepted:** the concern must be covered by bounded work.
- **Rejected:** the concern was reviewed and excluded for a recorded reason.
- **Deferred:** the concern is acknowledged but intentionally left outside the current plan.

An agent or model may propose a finding. It cannot assign the human dispositions.

## Rejection is useful evidence

Rejected findings should not disappear. A reviewer may reject a source file because a
new abstraction isolates it, or reject a consumer because a versioned contract preserves
compatibility. Keeping the reason prevents the next analysis from reopening the same
question without new evidence.

## Deferral must remain visible

Deferred work is not the same as rejected work. It represents known unresolved scope.
Planning readiness should not hide it. The owning authority must decide whether the
current change can proceed safely and where follow-up lives.

## Completeness checks the review boundary

Before planning, CIS requires findings, no remaining proposals, visible deferrals, and
no truncated traversal. This proves that the discovered set was dispositioned within
the declared bounds—not that the graph contains every possible impact.

## Accepted findings create coverage obligations

Plan validation requires every accepted impact to appear in bounded work. It also
rejects work that treats an unaccepted finding as approved scope.

The resulting plan can explain why each task exists.

## Takeaway

Let analysis widen attention, not authority. Preserve each finding's evidence and
identity, require human disposition, keep rejection and deferral rationale, and turn
only accepted impact into work.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis impact findings`](../manual/cis_impact_findings.md)
- [`cis impact completeness`](../manual/cis_impact_completeness.md)

