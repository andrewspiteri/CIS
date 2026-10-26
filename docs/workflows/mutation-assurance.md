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

Use the shared [`cis test mutation run`](../manual/cis_test_mutation_run.md) command
with a repository-owned JSON profile for any module. The profile declares native
commands, exact production scope, relevant test/configuration inputs, threshold and
limits. The default diagnostic kind exposes progress without claiming qualification.
A qualification runs its separate canary first and stops on failure.

CIS's own release-tier profile is `tools/mutation-profile.json`. Build the current
CLI with `dotnet build src/Cis.Host -c Release`, then use
`pwsh tools/run-mutation.ps1 -Canary` for its small native prerequisite or
`pwsh tools/run-mutation.ps1` for its full declared scope. The wrapper uses the same
shared service as other targets. Edit the reviewed profile to change scope or limits;
the former wrapper-specific output and resource switches have been replaced by those
profile fields. Preserve the adopted threshold when adapting the profile.
The .NET profile also disables background build-worker reuse in its target process,
so SDK workers do not outlive a completed native command. Other targets can declare
their own environment in the same shared profile contract.

Outputs are isolated beneath `.cis/local/testing/mutation/<profile-id>/`.
`latest-run.json` records every attempted run, including failures without a report.
`latest-result.json` retains the latest readable diagnostic or qualification report;
`last-qualification.json` retains the last passing full profile report. Hash-bound
sidecars preserve raw outcomes and run context. Reconciliation recomputes scores,
checks input hashes and rejects an older result as current after a later attempt.
Historical reports remain historical; a failed workflow remains failed.

The profile defaults to a 1,200-second wall limit per native command, 512 MiB for
one run, 1 GiB for the aggregate mutation subtree and a 1 GiB free-space reserve.
Storage sampling can overshoot between checks. Dependency caches and build output
outside owned mutation storage are outside its aggregate byte total. Native resource stops,
incomplete capture and cleanup failures hold qualification. The workflow table's
3,600-second timeout is an outer ceiling over the prerequisite and full command.
Native `TEMP`, `TMP` and `TMPDIR` default to a fresh, short owned directory beneath
mutation storage. The guard counts its bytes toward the command's run limit and
the aggregate retained limit; CIS removes it after execution. An explicit profile
override can place temporary files outside this budget; review that choice.

Successful attempts discard native scratch and capture, retaining a small summary
and the current profile reports needed for assessment. Failed attempts retain bounded
diagnostics until resolved or superseded. Failed-run native `logs/` capture keeps
at most 32 complete text/log files and 16 MiB combined; larger or excess files are
omitted without truncation. Temporary deployments and binary scratch are removed
after execution. The retention profile plans this
repository's `cis-safety/runs` family against 512 MiB, protecting its two newest
entries; it neither schedules deletion nor infers issue closure. Review cleanup after
writers finish. Additional profiles need their own reviewed retention-family paths.

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
   Record selected and executed case counts and elapsed time. Verify that early
   bail actually reduces the remaining queued work; a stop request alone does not
   prove saved work. If the adapter continues running queued cases, stop and repair
   test selection or adapter behavior before a broad campaign, then measure again.
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

- Native report: distinct status counts, raw score, exact report and configuration hashes.
- Conservative assessment: the adopted interpretation, threshold, denominator and
  unresolved findings; explain any difference from native counts.
- Execution health: baseline/canary result, expected bail, timeouts, crashes,
  cancellation, missing evidence and resource stops.

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

Treat iteration artifacts as transient. While investigating an open issue, keep
the failing native report, assessment, source commit plus relevant dirty diff,
exact command/configuration, tool versions and a small outcome/limitations summary.
Link existing artifacts instead of copying them into each task. A source hash alone
does not preserve an unavailable dirty change, so retain that small diff when needed.

Delete successful canary captures, duplicate reports and superseded diagnostic
attempts when they are no longer useful. Once the issue is fixed, remove its
remaining diagnostic artifacts; a brief outcome and commit reference can remain.
Do not create a permanent evidence bundle for each iteration. Keep raw evidence
for a current qualification or release only under its explicit retention requirement.
Cleanup is reviewed after writers finish; the runner does not detect issue resolution.

Full source/runtime copies, every host log and custom per-mutant proof packets are
not the default. Capture additional material only for a specific unresolved failure,
with an explicit scope, byte/time budget and retention decision. Preserve stronger
repository requirements where applicable.

Preflight both scratch and retained evidence against their budgets and free-space
reserve. Stop on an unmet limit; do not archive repeatedly to make live storage
appear bounded. Follow the [retention profile](../references/local-artifact-retention.md)
and reviewed artifact-cleanup plan. Cleanup remains an authorized action; never
silently delete protected evidence to start a run.
