---
title: "Finding Unexpected Work During Verification"
type: article
status: Draft
series: "CIS in Practice"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on verification change
summary: "How an unplanned workflow edit becomes a visible finding, scope decision, and possible improvement to future impact analysis."
cis:
  stable_id: change-impact-studio:article:unexpected-work-verification
---

# Finding unexpected work during verification

An agent completes a failed-test summary feature. Verification shows that `pr-ci.yml`
changed even though delivery workflow scope was never accepted.

## Preserve the fact

`cis verify compare` records the path as unexpected. The agent's explanation does not
remove the discrepancy.

## Inspect the reason

The workflow change may be required to retain logs, expose an artifact, or route failure
evidence. It may also be unrelated convenience work.

## Choose the consequence

A reviewer can revert it, revise impact and plan, or defer a follow-up with owner and
risk. If revised, affected validation reruns against the explicit new scope.

## Improve future context

If the change was legitimate, the missing relationship between test-summary behavior
and CI workflow becomes a learning proposal. After review, a graph declaration, planning
trigger, or task obligation can prevent recurrence.

## Takeaway

Unexpected change is not automatically a failure. It is evidence that requires a scope
decision and may reveal how the engineering knowledge model should improve.

## Canonical CIS sources

- [`cis verify compare`](../manual/cis_verify_compare.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)

