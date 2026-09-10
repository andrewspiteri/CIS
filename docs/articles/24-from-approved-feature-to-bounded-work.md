---
title: "From an Approved Feature Specification to Bounded Work"
type: article
status: Active
series: "Change Impact and Planning"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

When the product-definition wizard has been activated, feature authoring and validation
also bind the exact activation digest. A feature cannot combine requirements, architecture,
dictionaries, and delivery direction from different product-definition revisions.

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

## Authority carry-forward has strict conditions

Reusing approval is safe only when the operation preserves the exact approved meaning.
The source identity, path, digest, lifecycle, reviewer, rationale, product-definition
activation, requirement set, and target scope must still match. A title similarity or a
newer document that “looks equivalent” is not sufficient.

The distinction is between transformation and judgment. Turning stable requirement rows
into deterministic task inputs is a transformation. Deciding that a low-confidence
finding is acceptable scope is judgment. CIS can automate the first and must stop at the
second.

## Follow the derivation chain

For an approved invitation feature, derivation can:

1. validate the current feature and activation digest;
2. create a baseline-bound change dossier;
3. analyse the declared roots and requirements;
4. carry forward eligible deterministic findings;
5. generate the standard coordination and assurance spine;
6. activate surface-specific task types from positive requirement evidence;
7. create stable manual-test cases from functional requirements;
8. validate impact, decisions, dependencies, coverage, and generated artifacts; and
9. preserve the original approval basis for review.

The result is not an unrelated plan that happens to contain similar wording. Every task
can point back to the feature requirement and accepted impact that caused it to exist.

## Determinism does not mean inflexibility

Two features with different structured evidence should produce different workstreams.
A data-only feature should not receive empty frontend tasks; a public endpoint should
activate its caching and persistence-isolation obligations; a cross-surface feature should
create separate experience chains.

What remains deterministic is the mapping from reviewed evidence to task obligations.
Humans can revise the feature or use the explicit planning path when the standard mapping
does not fit. They do not need to approve unchanged boilerplate one item at a time.

## Stop conditions protect meaning

Suppose analysis reaches a dependency repository through a proposed edge, discovers an
unresolved API-version decision, or finds that the approved feature digest changed. A
fully automatic derivation would have to guess whether to include the dependency, choose
a contract strategy, or treat new content as already approved.

CIS stops instead. The reviewer can disposition impact, resolve the decision, reapprove
the source, or build a tailored plan. Automation has done the repeatable work and made the
new judgment visible.

## Atomicity protects the repository

Derivation touches several canonical files and catalog entries. If task generation
succeeds but test-case creation fails, leaving half a plan would create misleading
authority. Atomic rollback ensures the repository returns to its prior state and the
reviewer sees one failed operation rather than a partially approved-looking dossier.

Retry is safe only after the cause is addressed. Repeated execution against unchanged
inputs should remain idempotent rather than duplicating tasks or requirement coverage.

## Review the generated result proportionately

Exact carry-forward removes duplicate semantic approval, not quality control. Reviewers
can inspect whether every requirement has coverage, repository targets are correct,
dependencies are acyclic, test cases preserve acceptance criteria, and no stop condition
was suppressed. The review confirms the transformation and its current inputs; it does
not pretend to re-author the feature.

## Takeaway

Reuse human authority when the exact approved meaning remains current. Derive work
deterministically, preserve provenance, validate the complete result, and stop whenever
new judgment is required.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Manual test-case artifacts](../specs/manual-test-case-artifacts-spec.md)
- [`cis plan derive`](../manual/cis_plan_derive.md)
- [`cis plan import-spec`](../manual/cis_plan_import_spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
