---
title: "Expected, Missing, and Unexpected Change"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on verification-finding change
summary: "A practical vocabulary for reviewing whether implementation matches approved work."
cis:
  stable_id: change-impact-studio:article:expected-missing-unexpected-change
---

# Expected, missing, and unexpected change

A binary pass or fail hides the most useful part of scope verification: how the actual
implementation differs from the approved expectation.

## Expected and changed

The planned repository or path changed. This is necessary evidence, not proof that the
change is correct. Required validation still applies.

## Expected but missing

The plan required a contract, implementation, test, document, or artifact that is absent.
Tests can pass while this finding remains.

## Unexpected change

An unplanned path or repository changed. The result may reveal scope expansion, generated
noise, concurrent work, or an incomplete original impact analysis.

## Validation failed

The expected change exists, but a required deterministic check or evidence gate did not
pass. The implementation cannot be accepted merely because the diff looks right.

## Findings need disposition

Verification should preserve each discrepancy with task identity, evidence, and notes.
A reviewer can revise scope, request correction, accept a justified difference, or
record follow-up and residual risk.

## Takeaway

Describe the relationship between plan and implementation, not only a final color. The
vocabulary of expected, missing, unexpected, and failed makes completion gaps actionable.

## Canonical CIS sources

- [`cis verify diff`](../manual/cis_verify_diff.md)
- [`cis verify compare`](../manual/cis_verify_compare.md)
- [`cis verify validate`](../manual/cis_verify_validate.md)

