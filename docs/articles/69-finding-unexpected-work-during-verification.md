---
title: "Finding Unexpected Work During Verification"
type: article
status: Draft
series: "CIS in Practice"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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
remove the discrepancy. Agent run records, CI logs, test results, and security findings
can explain the change, but remain derived evidence until a reviewer changes the accepted
scope.

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

## Establish what actually changed

Start from the approved repository baseline and inspect status, diff, renames, deletions,
and untracked paths. Confirm whether `pr-ci.yml` changed in the candidate worktree, the
user's checkout, or both. Compare the provider result and workflow artifacts without
using either as the source of actual Git truth.

Isolation and baseline identity help determine whether the edit belongs to this attempt
or was pre-existing concurrent work.

## Ask why the path changed

Useful questions include:

- Which command or edit produced the workflow change?
- Which task obligation or newly discovered failure motivated it?
- Is the change necessary to retain test or security evidence?
- Does it alter permissions, secrets, triggers, environments, or release behavior?
- Could the same outcome be achieved within accepted scope?
- Which validation and reviewer now apply?

The agent's explanation guides investigation. The workflow diff and delivery policy
establish the consequence.

## Choose one governed outcome

If the edit is unrelated convenience work, remove it and verify that the intended feature
still passes. If it is required, revise impact and plan with the delivery surface,
security implications, owner, and evidence, then rerun affected validation. If the need is
real but separable, restore current scope and create a bounded follow-up.

Do not mark the path expected retrospectively without preserving the original finding and
rationale. The discrepancy is evidence about the quality of the plan.

## Expand assurance proportionately

A CI workflow can change token permissions, event triggers, artifact retention, network
access, and release behavior. Verification may need syntax validation, permission review,
representative local reproduction, safe remote-run inspection, and confirmation that no
secret values entered documentation or logs.

The feature's focused tests alone cannot accept that expanded risk.

## Improve future discovery carefully

If the workflow edit was legitimate, identify the missing relationship: perhaps the test
summary contract is produced by CI, or a verification task requires retained artifacts.
Propose an explicit graph edge, planning trigger, task obligation, or repository profile
update with the observed evidence.

Human review decides whether the proposal becomes canonical. One surprise should not
create a broad rule that every feature may change delivery workflows.

## Protect concurrent user work

If the unexpected workflow edit predates the agent attempt or belongs to another human,
do not revert or absorb it as part of the current task. Preserve the user's working tree,
compare the isolated candidate with its captured baseline, and report the overlapping path.
The current change may need a fresh baseline, a coordinated merge, or a separate task.

Unexpected does not mean disposable. Attribution is part of the evidence needed before
any corrective action.

## Takeaway

Unexpected change is not automatically a failure. It is evidence that requires a scope
decision and may reveal how the engineering knowledge model should improve.

## Canonical CIS sources

- [`cis verify compare`](../manual/cis_verify_compare.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Security testing and evidence](../specs/security-testing-and-evidence-spec.md)
