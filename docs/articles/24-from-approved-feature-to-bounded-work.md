---
title: "From an Approved Feature Specification to Bounded Work"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on feature-derivation change
summary: "How exact feature authority can produce impact, tasks, test cases, and a validated plan without creating a second approval."
cis:
  stable_id: change-impact-studio:article:approved-feature-to-bounded-work
---

# From an approved feature specification to bounded work

An approved feature specification already contains human authority. Asking a reviewer
to approve every deterministic restatement of that unchanged content adds ceremony but
not judgment.

CIS can carry exact feature authority into bounded work while stopping on exceptions.

## The feature needs stable requirements

A native feature specification declares functional requirements with stable IDs,
surfaces, acceptance criteria, and explicit frontend classification where applicable.
Alternative supported documents can supply numbered goals that receive deterministic IDs.

The plan records the source path and SHA-256 digest instead of copying the specification.

## Derivation checks authority first

`cis plan derive` verifies that the feature is current and explicitly approved. It
analyses impact, adopts only eligible deterministic findings, imports the task pack,
validates it, and preserves the original reviewer, rationale, path, and digest as the
approval basis.

It does not invent a new reviewer or plan rationale.

## Exceptions stop the shortcut

Derivation stops on stale or ambiguous approval, low-confidence or deferred findings,
truncated analysis, mismatched scope, provider conflicts, blocking decisions, or invalid
generated work.

The explicit impact, disposition, import, validation, and approval commands remain
available for those cases.

## The operation is atomic

If derivation fails, prior dispositions, generated files, and catalog state are restored.
A partially generated plan cannot appear approved.

## Test artifacts are part of planning

Each functional requirement produces stable manual-test cases in Markdown and CSV from
one deterministic model. The artifacts retain the feature digest and coverage identity.
Actual execution results remain verification evidence rather than rewriting the cases.

## Takeaway

Reuse human authority when the exact approved meaning remains current. Derive work
deterministically, preserve provenance, validate the complete result, and stop whenever
new judgment is required.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Manual test-case artifacts](../specs/manual-test-case-artifacts-spec.md)
- [`cis plan derive`](../manual/cis_plan_derive.md)
- [`cis plan import-spec`](../manual/cis_plan_import_spec.md)

