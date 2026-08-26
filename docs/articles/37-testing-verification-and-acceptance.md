---
title: "The Difference Between Testing, Verification, and Acceptance"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on assurance change
summary: "Tests exercise behavior, verification checks the approved delivery obligation, and acceptance applies human authority to the evidence."
cis:
  stable_id: change-impact-studio:article:testing-verification-acceptance
---

# The difference between testing, verification, and acceptance

These three activities support each other, but they answer different questions.

## Testing

Testing asks whether specified behavior works under exercised conditions. Unit,
integration, contract, browser, architecture, and operational tests provide different
forms of evidence.

A test proves only what it actually asserts. Its name and pass status cannot establish
uncovered behavior.

## Verification

Verification asks whether the delivered change satisfies the approved engineering
obligation. It includes tests, but also scope comparison, documentation, contracts,
design artifacts, migrations, security, operations, and missing or unexpected work.

## Acceptance

Acceptance is the human decision that current evidence is sufficient and residual risk
is acceptable. A tool can validate structural readiness; it cannot invent the reviewer
or rationale.

## Why separation matters

If testing implies acceptance, a passing suite can conceal an incomplete plan. If
verification implies acceptance, a structurally valid evidence pack can accept risk
without an owner.

## Takeaway

Use tests to establish behavior evidence. Use verification to compare the complete
delivery with approved scope. Use human acceptance to decide whether the evidence and
remaining risk are sufficient.

## Canonical CIS sources

- [Testing standard](../standards/testing-standard.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Verification task type](../specs/verification-task-type.md)
