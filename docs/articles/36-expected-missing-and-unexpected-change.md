---
title: "Expected, Missing, and Unexpected Change"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

## The categories are observations, not verdicts

“Expected and changed” sounds positive, but a changed file can contain the wrong behavior.
“Unexpected” sounds negative, but the path may be essential work that impact missed. The
classification describes the relationship between plan and repository; review determines
meaning and consequence.

This neutral vocabulary prevents a comparison tool from granting authority merely by
matching patterns.

## Work through a concrete comparison

An invitation feature plans changes to an API handler, permission row, customer screen,
integration tests, and verification record. The actual diff contains:

- the handler, permission row, and customer screen: expected and changed;
- no integration-test change: expected but missing;
- a CI workflow edit: unexpected change; and
- a failing API compatibility result: validation failed.

The feature is not summarized as simply red or green. The reviewer can ask why tests are
missing, whether the workflow edit is necessary to retain evidence, and which contract
strategy resolves the compatibility failure.

## Missing work has several forms

A missing finding may be a path that did not change, a required artifact that was never
produced, a test suite that did not run, a manual review without an owner, or an accepted
impact with no task coverage. Verification should retain the specific obligation rather
than collapsing all absence into “incomplete.”

Unavailable evidence is also distinct from failed evidence. A browser test that could not
run because the environment was absent has a different recovery path from a browser test
that ran and failed.

## Unexpected work needs attribution

An unexpected path can come from the executor, a generator, concurrent user work,
baseline contamination, framework side effects, or local artifacts. Exact baseline and
run isolation help distinguish them. The executor's explanation is useful, but Git and
tool evidence establish what occurred.

If the work is legitimate, revise accepted impact and plan through the governed path and
rerun affected checks. Do not simply relabel the path expected after the fact; preserve
the original discrepancy and its disposition.

## Validation failures need result semantics

Process exit code is not always enough. A test or security command may return zero without
the expected structured artifact, or an adapter may be unable to parse the result. CIS
treats that as invalid evidence. A real failing assertion, malformed result, timeout, and
unavailable tool should remain distinguishable because they imply different next actions.

## Closure requires explicit disposition

Each missing, unexpected, or failed finding needs an owner and consequence. Correction,
scope revision, justified acceptance, deferral, and follow-up are different outcomes. The
verification record should show which was chosen, by whom, and against which evidence.

## Use explicit closure outcomes

| Finding | Possible closure |
|---|---|
| Expected but missing | Implement it, revise the obligation with authority, or defer with risk |
| Unexpected change | Revert it, accept revised scope, or separate it into another change |
| Validation failed | Correct the behavior, repair invalid evidence, or record an authorized exception |
| Evidence unavailable | Restore the capability, provide governed alternative evidence, or accept residual risk |

The available outcome depends on policy. A required security check may not permit a
general exception, while a platform-specific manual check may allow bounded deferral.

## Takeaway

Describe the relationship between plan and implementation, not only a final color. The
vocabulary of expected, missing, unexpected, and failed makes completion gaps actionable.

## Canonical CIS sources

- [`cis verify diff`](../manual/cis_verify_diff.md)
- [`cis verify compare`](../manual/cis_verify_compare.md)
- [`cis verify validate`](../manual/cis_verify_validate.md)
