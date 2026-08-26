---
title: "Deterministic, Manual, and Advisory Enforcement"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on standards-enforcement change
summary: "Different rules need validators, tests, human review, or model advice, and those evidence strengths must remain distinct."
cis:
  stable_id: change-impact-studio:article:enforcement-evidence-types
---

# Deterministic, manual, and advisory enforcement

Not every engineering expectation can be enforced by the same mechanism.

## Deterministic evaluators

Parsers and validators can establish metadata, path, schema, catalog, or contract facts
with stable pass and fail semantics.

## Tests and architecture tests

Behavior tests exercise runtime contracts. Architecture tests enforce structural
boundaries such as prohibited dependencies.

## Manual review

Some rules require contextual judgment: usability, risk, rationale quality, or whether
an exception is acceptable. The evidence needs a named reviewer and reason.

## Advisory models

Models can identify likely duplicates, conflicts, or suspicious patterns. Their output
remains a candidate until confirmed by deterministic evidence or explicit human review.

## Not mapped

The absence of an evaluator is a visible gap, not proof of compliance and not an
automatic exception.

## Takeaway

Match each rule to the strongest appropriate evidence. Preserve the distinction between
deterministic result, test evidence, human judgment, model advice, and enforcement gaps.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Standards conformance matrix](../references/standards-conformance-matrix.md)
