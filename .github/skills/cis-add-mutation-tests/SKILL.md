---
name: cis-add-mutation-tests
description: Use mutation testing to challenge important unit-test assertions. Use when risk or independent assurance requires evidence beyond line coverage.
---

# CIS Add Mutation Tests

## Purpose

Find behavior that tests execute but do not meaningfully assert, after checking
that the native mutation runner and fixtures can execute the selected scope.

## Workflow

1. Read the adopted mutation profile, threshold and stronger evidence rules. Fix
   source/configuration/tool identities, scope, wall time, maximum diagnostic
   attempts, artifact budget and free-space reserve before execution.
   Use `cis test mutation run --profile <path> --kind diagnostic` for a bounded
   native iteration and `--kind qualification` for the complete declared profile.
   Keep relevant tests/build/tool configuration in the profile's input pins; target
   details belong in profiles rather than new mutation controllers.
   When compilation dominates, group sources with a shared compilation and compatible
   test context in one native invocation. Use the generic profile command above or
   the documented direct native route; the incremental service currently selects
   mutation work one source file at a time. Include zero-definition files in scope
   without a separate compilation for each.
   For governed work, preserve inherited `CIS_WORK_*` authority and its live parent,
   or pass the existing `--change`, `--task`, `--actor` and linked `--authority`.
   Inspect `cis agent work status <change> --task <task>` and total owned storage;
   profile limits never allocate new time, attempts or bytes. Missing ownership
   holds before preparation. Non-adopted diagnostics remain explicitly unaccounted.
2. Qualify a small representative canary through the native mutation runner: healthy
   baseline passes; an intentional mutant produces the intended failed test. Include
   a real child-process/transport path when relevant, with actual cleanup. Record
   selected and executed case counts and elapsed time. Verify that early bail
   reduces the remaining queued work; a stop request alone does not prove saved
   work. If the adapter continues running queued cases, stop and repair test
   selection or adapter behavior, then measure again before a broad campaign.
   Stop on a failed prerequisite before any broad campaign.
   Also demonstrate a known survivor/control, rejection of an infrastructure failure
   and healthy restored execution. Qualify coverage-analysis and early-bail changes
   on the actual adapter/async path. A positive kill count alone is insufficient.
   Estimate the full-run budget from the pilot before expansion.
   Retain these controls through the existing `--kind canary` route with a
   representative profile's `correctnessPilot` definition; use
   `correctnessPilotProfile` on the target diagnostic/qualification/incremental
   profile. The first actual outcome needs the existing controller's substantive
   `agent work progress` assessment, including `native-pilot:<basis>` evidence,
   before expansion. This is routine controller reflection, not a new human gate.
   Native reports, actual launch references and relevant runner/adapter/fixture/
   configuration/tool/environment inputs determine currentness; a Boolean or
   synthetic report cannot establish governed readiness. Compatible proof remains
   reusable across unrelated production edits. Unknown or mismatched native paths
   hold rather than borrowing a different runner's proof. Keep the ordinary narrow
   positive-kill canary separate; a child round trip qualifies only the actual path
   observed and does not prove arbitrary cross-process mutant activation.
3. Use native early bail by default. Distinguish runner-recorded intentional bail
   from timeout, crash, cancellation and missing protocol evidence. Do not require
   every selected test to execute after a mutant has been detected.
4. Run one bounded diagnostic hypothesis at a time. Investigate shared setup,
   selection fan-out, host startup and cleanup when execution dominates. Stop at the
   declared iteration budget or a repeated failure without new information.
5. Compare distinct mutant outcomes in the same source scope. Diagnostic row counts
   are not additional kills. Review survivors against exact source and contracts;
   never invent equivalence, exclusions or accepted limitations.
6. Record native counts, conservative assessment and execution health separately.
   Normal native assertions and failed-test exceptions need no bespoke whitelist or
   every-row direct-assertion proof; preserve actual infrastructure failures and any
   stronger adopted policy.
   Keep `validEvidence`, `thresholdMet` and `qualified` distinct. A valid report at
   the unchanged threshold may remain unqualified under a genuine required-child,
   infrastructure, restoration or cleanup hold. Incremental receipts and partial
   diagnostics always remain unqualified. Native assertion-message changes or an
   alternative legitimate detecting test do not erase a native kill.
7. After focused improvements and a healthy runner, execute the full required scope
   for the final gate with its unchanged threshold. A canary, partial run or timeout
   cannot satisfy that gate.

## Completion evidence

Link a compact record of source commit/dirty diff, command/configuration, tool
versions, native report and hashes, distinct status counts, execution failures,
unrun checks and residual risk. Keep native reports unchanged. Add bounded failure
diagnostics only when needed; source/runtime copies and per-mutant controllers are
not routine evidence.
Bind relevant production, test, build/configuration and tool inputs, including
reproducible relevant untracked inputs. For disjoint native groups, retain one
unchanged report per group and apply the adopted complete-scope threshold to summed
outcomes rather than averaging percentages. Repository receiving rules must verify
complete scope and current inputs before any pilot report contributes to that gate.
Include platform identity in one compact candidate manifest. Keep timeouts and
unresolved misses explicit without kill credit.
Use compact candidate-bound miss rows naming the definition/operator/span/replacement,
raw status, requirement, decision and authorized reviewer; an approval Boolean alone
does not establish review authority. Keep these rows in one reviewed miss table.
Successful captures are transient: discard them after assessment. Keep failed
diagnostics only while useful for an unresolved issue, then remove resolved,
superseded and duplicate attempts under the repository retention policy. A later
failed or running attempt must not leave an older passing score looking current.
Record preparation, native execution and result-admission costs separately, with
warm and edited-code increments distinct from completed affected regression.
Overlapping parent/child durations are not additive and an unfinished cutoff is
not a completed measurement. If restoration is unresolved, resume only the original
pinned recipe under its retained work reference; `--recovery-of` requests bounded
restoration and healthy postflight, not another canary or campaign. Restored health
cannot manufacture missing correctness observations.

## Guardrails

Do not hide survivors, relax thresholds or convert timeouts into kills. Preflight
scratch and retained-storage caps; stop rather than accumulating archives or
deleting protected evidence without authorization. Do not repeat a broad campaign
until the failed prerequisite or hypothesis changes. Stronger adopted policy wins.
