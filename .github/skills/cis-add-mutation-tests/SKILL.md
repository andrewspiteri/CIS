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
2. Qualify a small representative canary through the native mutation runner: healthy
   baseline passes; an intentional mutant produces the intended failed test. Include
   a real child-process/transport path when relevant, with actual cleanup. Record
   selected and executed case counts and elapsed time. Verify that early bail
   reduces the remaining queued work; a stop request alone does not prove saved
   work. If the adapter continues running queued cases, stop and repair test
   selection or adapter behavior, then measure again before a broad campaign.
   Stop on a failed prerequisite before any broad campaign.
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
7. After focused improvements and a healthy runner, execute the full required scope
   for the final gate with its unchanged threshold. A canary, partial run or timeout
   cannot satisfy that gate.

## Completion evidence

Link a compact record of source commit/dirty diff, command/configuration, tool
versions, native report and hashes, distinct status counts, execution failures,
unrun checks and residual risk. Keep native reports unchanged. Add bounded failure
diagnostics only when needed; source/runtime copies and per-mutant controllers are
not routine evidence.
Successful captures are transient: discard them after assessment. Keep failed
diagnostics only while useful for an unresolved issue, then remove resolved,
superseded and duplicate attempts under the repository retention policy. A later
failed or running attempt must not leave an older passing score looking current.

## Guardrails

Do not hide survivors, relax thresholds or convert timeouts into kills. Preflight
scratch and retained-storage caps; stop rather than accumulating archives or
deleting protected evidence without authorization. Do not repeat a broad campaign
until the failed prerequisite or hypothesis changes. Stronger adopted policy wins.
