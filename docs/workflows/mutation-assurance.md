---
title: "CIS mutation assurance workflow"
type: workflow-definition
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on mutation scope, threshold, runner, or evidence change"
cis:
  stable_id: change-impact-studio:workflow:mutation-assurance
---

# Mutation assurance workflow

Qualify the native runner on a small representative scope before spending time on
a broad mutation campaign. Use bounded diagnostic iterations to improve tests or
execution, then run the complete declared scope for the release gate. A canary or
partial diagnostic result cannot satisfy that gate.

This release-tier entry runs separately from ordinary pull-request verification.
Native Stryker reports remain unchanged derived evidence under
`.cis/local/testing/mutation/`; assessments and summaries are separate files.

Build the current CLI with `dotnet build src/Cis.Host -c Release` before using the
repository runner. `pwsh tools/run-mutation.ps1 -Canary` runs only the small native
canary. The normal entry runs that canary first and stops if it fails, before
starting the complete configured scope. Canary reports never replace the full-scope
report. Native SDK discovery and a healthy initial test run are prerequisites.

`tools/run-mutation.ps1` defaults to a 1,200-second wall limit,
512 MiB for one run, 1 GiB for the retained mutation subtree and a 1 GiB free-space
reserve. Override these explicitly with `-TimeoutSeconds`, `-MaximumRunBytes`,
`-MaximumRetainedBytes` and `-MinimumFreeBytes`. It samples storage once a second,
so a fast writer can overshoot a limit between checks. These controls cover the
declared mutation output; dependency caches and other build scratch remain outside
that byte total. Resource stops fail qualification and preserve the small run record.
The workflow table's 3,600-second timeout is an outer ceiling; the runner's earlier
1,200-second full-run limit applies unless explicitly overridden. Its prerequisite
canary is separately capped at 120 seconds and 128 MiB.

The retention profile separately plans the transient `runs` family against 512 MiB,
protecting its two newest entries. It does not automatically delete them. If those
entries exceed the policy, review the plan and storage decision before another run.

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
|---|---|---|---|---|---|---:|
| cis-safety-mutation | pwsh tools/run-mutation.ps1 | . | cis-safety-mutation | - | no | 3600 |

## Before a broad campaign

1. Fix the source revision, dirty diff including relevant untracked files, production
   scope, test selection, tool versions, configuration and existing threshold.
   Set a wall-clock limit, maximum diagnostic attempts, per-run artifact budget,
   retained-storage budget and free-space reserve before execution.
2. Run a small representative canary through the adopted **native mutation runner**:
   a healthy baseline must pass, and an intentional mutation must produce the
   intended failed test. Include a real child-process/transport path when the scope
   depends on it. Check selected tests, startup, reporting, cleanup and termination;
   a plain test-runner pass alone does not qualify the mutation runner.
3. Stop on a failed canary or resource preflight. Diagnose the smallest failing
   selection before expanding scope. Do not start another broad campaign to
   investigate a runner or fixture that already fails this prerequisite.

## Diagnostic iterations

Use native early bail after a failing test by default; do not require every selected
test to keep running after it has detected the mutant. Runner-recorded intentional
bail is distinct from timeout, crash, external cancellation or lost protocol data.
Missing results alone do not prove intentional bail. A complete final campaign must
account for the declared mutant scope under the adopted policy, not execute every
remaining test after each detected mutation.

Each diagnostic attempt must test a stated hypothesis with a bounded scope. Stop
when the attempt, time or storage budget is exhausted, or when the same failure
repeats without new information. Record the blocker and next decision instead of
repeating an identical campaign. Expand only after relevant changes or evidence
justify it. Healthy tests can still have poor mutation behavior: inspect shared
setup fan-out, cold-host overhead, fixture waits and cleanup before adding more
assertions or increasing timeouts.

Measure progress by **distinct detected mutants in a fixed scope**, not by the number
of diagnostic rows, assertions or copied artifacts. Repeated failures from one
shared setup defect are one mutation finding. Link surviving definitions to actual
source and contract facts; do not invent equivalence, exclusions or accepted
limitations to shrink the denominator.

## Checkpoint and final gate

Keep three results visible in the small checkpoint summary:

| Result | Record |
| --- | --- |
| Native report | Distinct status counts, raw score, exact report and configuration hashes. |
| Conservative assessment | The adopted interpretation, threshold, denominator and unresolved findings; explain any difference from native counts. |
| Execution health | Baseline/canary result, expected bail, timeouts, crashes, cancellation, missing evidence and resource stops. |

Ordinary native failed assertions and failed-test exceptions are sufficient native
outcome evidence. Do not introduce bespoke exception whitelists, per-mutant
controllers or a requirement that every returned row be a direct assertion. Inspect
actual infrastructure/cleanup failures separately. Stronger already-adopted evidence
policy still applies; changing it requires its own reviewed change and cannot
retroactively qualify an old run.

Only the final full-scope run with valid evidence and the unchanged required
threshold may satisfy the release gate. Keep its raw report, conservative assessment
and execution-health result separate. Never turn a diagnostic pass, timeout or
recovered artifact into gate success.

## Compact evidence and storage

Retain the source commit plus relevant dirty diff, exact command/configuration and
tool versions, native report, hashes and a small outcome/limitations summary. Link
existing artifacts instead of copying them into each task. A source hash alone
does not preserve an unavailable dirty change, so retain that small diff when needed.

Full source/runtime copies, every host log and custom per-mutant proof packets are
not the default. Capture additional material only for a specific unresolved failure,
with an explicit scope, byte/time budget and retention decision. Preserve stronger
repository requirements where applicable.

Preflight both scratch and retained evidence against their budgets and free-space
reserve. Stop on an unmet limit; do not archive repeatedly to make live storage
appear bounded. Follow the [retention profile](../references/local-artifact-retention.md)
and reviewed artifact-cleanup plan. Cleanup remains an authorized action; never
silently delete protected evidence to start a run.
