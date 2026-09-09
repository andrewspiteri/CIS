---
title: "Comparing Approved Impact with the Actual Git Diff"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on verification change
summary: "How planned repositories and paths are compared with observed workspace changes from the approved baseline."
cis:
  stable_id: change-impact-studio:article:approved-impact-actual-diff
---

# Comparing approved impact with the actual Git diff

Verification becomes meaningful when the expected change is recorded before
implementation and compared with the repository afterward.

## The approved side

The change dossier preserves the baseline, accepted impact, work items, target
repositories and paths, expected documents and contracts, and validation obligations.

## The actual side

CIS observes Git state from the same baseline. It does not infer actual change from task
status or an external issue.

## Comparison exposes asymmetry

The comparison can show planned work that is missing and unplanned work that occurred.
Both matter. A clean implementation of only half the plan is incomplete; a successful
feature accompanied by an unexplained workflow edit has expanded scope.

## Dossier edits are governance evidence

Authority-repository dossier changes are expected records of the workflow and are not
automatically treated as unplanned participant implementation.

## Review decides consequence

An unexpected path may be a defect, harmless tool output, or a legitimate missing impact.
Verification preserves the fact; a reviewer dispositions the consequence.

## Compare at the right level

Approved impact may refer to a stable contract, component, requirement, or repository,
while Git reports paths. Verification needs an explicit mapping between them. A task that
targets an API operation may legitimately change its handler, inventory row, OpenAPI
artifact, tests, and documentation. Checking only for one expected filename would be too
narrow; accepting every file in the repository would be too broad.

The plan should name known paths and semantic obligations before execution. The graph and
ownership map can help translate stable targets into path expectations, but the actual
diff remains the observed evidence.

## Build a comparison matrix

For each approved work item, record:

- accepted impact and requirement IDs;
- owned repository and expected path patterns;
- expected canonical documents or contract rows;
- validation and artifact obligations;
- actual changed, deleted, renamed, and added paths; and
- discrepancies requiring disposition.

This exposes both directions of failure. A new test file does not compensate for a missing
contract update, and an expected handler edit does not justify an unrelated workflow
change.

## Account for multi-repository scope

Each product-owned repository has its own baseline and actual diff. A workspace result
should not report success after observing only the authority repository or the first
available participant. Missing or stale repositories remain explicit evidence gaps.

Dependency repositories are different: they can inform impact, but the current workspace
must not accept write-capable changes to them as ordinary task output. A required
dependency modification becomes a coordination finding under that product's authority.

## Treat dossier changes carefully

Proposal, impact, plan, task, and verification files evolve as the governed workflow
progresses. Their expected mutation should not be confused with unplanned implementation.
That exclusion must be specific to managed dossier paths; it must not hide edits to
technical intent, standards, or reference inventories that may change product authority.

## Disposition discrepancies with evidence

An unexpected migration might reveal a real omission in impact. A missing planned client
change might be correct because compatibility was preserved through the chosen API
strategy. A generated snapshot might be noise or a required reviewed artifact.

The reviewer records the consequence: revise scope and rerun affected validation, correct
the implementation, accept a justified difference, or create bounded follow-up with
residual risk. Verification preserves the original comparison so the plan is not silently
rewritten to match whatever happened.

## The diff is necessary but not sufficient

A path can change without satisfying its requirement. Conversely, a requirement may be
satisfied through an implementation path the plan did not predict. The comparison locates
those questions; tests, contract evidence, design review, and human judgment answer them.

## Follow a representative mismatch

Suppose a plan accepts a public API, customer screen, permission, and test impact. Git
shows the API and screen, no permission inventory change, a new infrastructure file, and
updated tests. The matrix identifies one missing authority record and one unexpected
delivery surface. The reviewer can determine whether permission behavior remained
unchanged, whether the inventory was omitted, and whether infrastructure became necessary
through a newly discovered dependency.

Only after those questions are dispositioned should the comparison be considered closed.
Editing the plan to list the infrastructure file without preserving the discovery would
erase evidence that the original impact model missed a relationship.

## Takeaway

Freeze the expectation, observe Git independently, and compare the two explicitly. That
turns scope control into inspectable evidence rather than trust in the executor.

## Canonical CIS sources

- [`cis verify compare`](../manual/cis_verify_compare.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
