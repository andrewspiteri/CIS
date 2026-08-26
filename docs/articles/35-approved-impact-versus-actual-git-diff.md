---
title: "Comparing Approved Impact with the Actual Git Diff"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

Freeze the expectation, observe Git independently, and compare the two explicitly. That
turns scope control into inspectable evidence rather than trust in the executor.

## Canonical CIS sources

- [`cis verify compare`](../manual/cis_verify_compare.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)

