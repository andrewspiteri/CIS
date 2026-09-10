---
title: "From Proposed Impact to Approved Scope"
type: article
status: Active
series: "Change Impact and Planning"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

## Findings should describe consequences

A good finding states a consequence, not merely a matched file. “API inventory row
`INVITE-POST` may change its error contract” is reviewable. “Found `invite` in 37 files”
is routing evidence that still needs interpretation. The finding should explain the path
from the proposal root to the affected target and expose any weak or truncated edge.

These states answer different questions. Accepted means the current change owns a
coverage obligation. Rejected means the evidence was considered and does not belong to
this change. Deferred means the concern is real but intentionally remains outside the
current delivery boundary. Proposed means no authority has made that choice yet.

## Review one finding at a time

A useful disposition process asks:

1. Is the target correctly identified and current?
2. Does the evidence path support the claimed consequence?
3. Is the target owned by this product or only dependency context?
4. Does an existing contract or abstraction contain the change?
5. If accepted, what kind of work and evidence will cover it?
6. If rejected or deferred, what rationale will help a later reviewer?

Batch approval may be appropriate for deterministic, homogeneous findings with the same
authority. Material security, compatibility, data, or ownership findings deserve
individual attention even when an analyser reports high confidence.

For example, a graph may reach a legacy serializer through a historical relationship.
The reviewer can reject the finding because the active API version no longer calls that
path, citing compiler and contract evidence. If a later build observes a new call, the
changed evidence justifies reopening the finding. The earlier rejection is not blindly
permanent; it is a decision bound to evidence.

## Make deferral actionable

A useful deferral names the owner, consequence, review point, and follow-up vehicle. “Out
of scope” alone hides risk. “Bulk revocation is deferred to CIS-0042; single-link
revocation ships without it, and support must retain manual recovery” gives planning and
acceptance something concrete to evaluate.

Completeness should report unavailable repositories, weak evidence, and rejected or
deferred counts. A green state with three documented deferrals is useful; a green state
that suppresses them is misleading.

## Re-analysis preserves decisions carefully

When the graph changes, stable target identities let CIS compare new findings with prior
review. Unchanged evidence can retain a disposition only when the approved boundary and
source digests still match. New targets begin proposed. Changed evidence should warn or
reopen review rather than carrying authority forward because the text looks similar.

This is where stable IDs earn their cost: they preserve human reasoning without turning
an old approval into a blanket policy for future baselines.

## Scope approval is not implementation approval

Accepting an impact means “this concern must be addressed.” It does not select the
solution, waive standards, approve generated tasks, or accept completion. Decisions and
planning still determine how the concern is handled; verification later determines
whether the result matches that obligation.

The reverse mapping matters as well. A proposed cleanup task with no accepted impact,
requirement, decision, or delivery obligation may be worthwhile, but it is not silently
part of the approved change. It should receive its own justification or separate dossier.

## Takeaway

Let analysis widen attention, not authority. Preserve each finding's evidence and
identity, require human disposition, keep rejection and deferral rationale, and turn
only accepted impact into work.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis impact findings`](../manual/cis_impact_findings.md)
- [`cis impact completeness`](../manual/cis_impact_completeness.md)
