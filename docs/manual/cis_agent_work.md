---
title: "cis agent work"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-work
---

# `cis agent work`

Allocate the work allowed for a change and its logical tasks, inspect consumption, and record a bounded continuation through the headless CLI. Starting a new run, changing providers, or resuming a run does not restore spent work.

## Scope and authority

Direct `agent run` and direct-task `agent resume` require work accounting when the authority has adopted engineering defaults with `requireTaskCompletion: true`. A provider profile alone does not adopt these controls. Repositories without that adoption retain their existing direct-run behavior; minimal import does not acquire a new mandatory allocation. Product-document authoring and story execution are outside this accounting slice.

The controller supplies an allocation from the existing authorized planning or controller process. The actor and decision reference record that authority; provider output, a resume reason, and `--approve-requests` do not grant it. An absent allocation on the governed direct route holds execution and identifies the missing allocation decision. Allocating work does not approve a plan, accept impacts, satisfy a design barrier, or transition a task.

One authority-owned record retains the containing change, logical accounting owners, allocations, measured consumption, reservations, restoration reserve, logical attempts, checkpoints, and recovery uncertainty. CIS diagnostic cleanup protects `.cis/local/agents/work-control.json`, `work-control-adoption.json`, and `work-control.lock` under that same directory. Those three accounting, adoption, and transaction-lock files are distinct from disposable run diagnostics even though the existing agents retention family covers their parent directory. Raw external deletion is outside that cleanup guarantee. Do not edit or delete these files to reset work: missing or corrupt accounting after adoption holds execution and requires reconciliation, not a new empty allocation.

Historical runs remain readable. Initial allocation must explicitly acknowledge that earlier unmeasured effort is unknown; it does not assert that those runs cost nothing. The accounting guarantee covers supported controllers sharing this authoritative state. It does not supervise independent hosts or raw external agent commands. Storage owner and ceiling references are recorded; complete storage enforcement is outside this slice.

## Commands

```text
cis agent work allocate <change-id> --input <allocation.json> --actor <identity> --reason <rationale>
cis agent work status <change-id> [--task <task-id>]
cis agent work progress <change-id> <task-id> --input <checkpoint.json> --actor <identity>
cis agent work disposition <change-id> <task-id> --input <disposition.json> --actor <identity> --reason <rationale>
```

All four commands accept `--repo <authority-path>` and `--format <human|json|agent>`. `status` is read-only. The other operations check current authority and the input revision before changing work state. Request files contain UTF-8 JSON; use the current `work.revision` from status for an update rather than modifying internal accounting files.

### Allocate

Allocation values describe active time in seconds and logical-attempt limits. CIS reports measured capacity in milliseconds. A logical attempt is a bounded hypothesis investigation or planned delivery step; a provider invocation, command, or expected failing development test is not automatically a new logical attempt.

The first allocation supplies:

| Field | Meaning |
| --- | --- |
| `changeSeconds`, `changeAttempts` | Authorized containing-change allowance. |
| `restorationSeconds` | Capacity retained for necessary restoration. |
| `tasks` | Existing task identities and their `seconds`, `attempts`, and `restorationSeconds`. The reserve defaults to zero only when that does not reduce an existing reserve. |
| `tasks[].fixed` | A separately fixed task limit; defaults to `true`. |
| `tasks[].allowRedistribution` | Whether remaining task allocation may be redistributed within delegated authority; defaults to `false`. |
| `decisionReference` | Existing policy or controller allocation decision. |
| `storageCeilingReference` | Applicable storage ceiling reference, without implying full enforcement. |
| `controllers` | Identities delegated to operate this allocation. |
| `acknowledgeHistoricalUnknown` | Explicit acknowledgment required for the initial unmeasured historical baseline. |
| `expectedRevision` | Omit on initial allocation; supply the current revision on updates. |

Updates preserve consumption, reservations, restoration capacity, history, and fixed limits. Delegated redistribution must fit the unchanged outer allowance. The original allocation authority may grant an eligible new root task capacity from the change's unallocated time and logical attempts without altering existing fixed task caps. This does not depend on permission to redistribute another owner's allowance, and it does not clear that owner's holds. A new filename, task decomposition, provider, profile, or source fingerprint cannot create another allowance for the same containing work.

An ordinary update cannot reduce a task's protected restoration reserve, even when `fixed` is `false` and `allowRedistribution` is `true`. Repeat the existing `restorationSeconds` when updating that task. A lower value, including an omitted field that defaults to zero while a nonzero reserve exists, is rejected with an actionable error. Transfer only unreserved capacity; task consumption, active reservations, and the containing-change reserve remain protected.

This example is synthetic. Replace its values and decision references with the actual authorized allocation, existing task identity, and controller. It does not establish a production allowance.

```json
{
  "changeSeconds": 7200,
  "changeAttempts": 6,
  "restorationSeconds": 300,
  "tasks": [
    {
      "taskId": "WORK-090",
      "seconds": 3000,
      "attempts": 3,
      "restorationSeconds": 120,
      "fixed": true,
      "allowRedistribution": false
    }
  ],
  "decisionReference": "controller-allocation:synthetic-example",
  "storageCeilingReference": "approved-storage-policy:synthetic-example",
  "controllers": ["Authorized controller"],
  "acknowledgeHistoricalUnknown": true
}
```

Save it as `work-allocation.json`, then use the existing approved task workflow:

```powershell
cis agent work allocate CIS-0001 --input .\work-allocation.json --actor "Authorized controller" --reason "Record the authorized change and task allowance" --format json
cis agent work status CIS-0001 --task WORK-090 --format json
cis agent prepare CIS-0001 WORK-090 --provider claude --format agent
cis agent run CIS-0001 WORK-090 --provider claude --mode implement --permission workspace-write --actor "Authorized controller" --timeout-seconds 600 --format json
```

Provider permissions, explicit command rules, plan eligibility, isolation, and current evidence still apply. A launch reserves task and change capacity before expensive preparation. The requested total bound is at least 30 seconds and must fit the provider profile, remaining task and change allowance, and restoration reserve. The reviewed profile permits at most 3,600 seconds per run. Known preparation uses part of that same bound; the provider receives its positive remainder, preserving fractional seconds. There is no second 30-second minimum after preparation. Startup and idle limits are bounded by that remainder. An oversized request or exhausted remainder holds before provider invocation. Inspect the returned hold and supported next action when less capacity remains.

Recovery uncertainty holds conflicting work for the affected accounting owner. Other independent owners may proceed when their own authority and holds permit it and the remaining shared change capacity still fits. The uncertain reservation continues to count against that shared allowance.

### Record progress

Record reflection at the first useful result, before broader work, and within the adopted execution window of cumulative active work. The reviewed provider profile sets that window to 3,600 seconds; an applicable narrower window governs instead. CIS checks at the next safe admission boundary, rather than interrupting a healthy operation or starting a timer for each command. Once cumulative measured work since the last qualifying controller checkpoint reaches the window, another expensive run or resume returns a non-success hold directing the controller to `agent work progress`.

This is routine execution work, not another approval or reviewer call. A valid controller checkpoint can keep the same logical attempt open with `closeAttempt: false`; it does not transition the canonical task to `Blocked`. New run IDs, provider changes and resumed provider-attempt numbers do not reset the window or spent work. Automatic settlement summaries, disposition summaries, status reads and model success claims do not qualify as controller reflection. Existing controller records are reused only when their provenance and required content identify them unambiguously.

One qualifying checkpoint comes through the controller progress operation and records Outcome, nonempty Evidence, Approach, Decision, and the next bounded action. CIS records its origin and measured Cost context; the checkpoint cannot overwrite consumption. Optional `hypothesis` and `expectedResult` identify the investigation. Set `closeAttempt` only when that logical attempt has concluded. A `blockerKey` identifies a repeated blocker; `materialNewInformation` records the controller's judgment rather than asking a semantic classifier to infer progress. Recording reflection permits continuation automatically when all other gates allow it; it does not clear repeated-blocker, allocation, recovery, permission or required-review holds.

For example, read the current revision and write one input file:

```powershell
$workStatus = cis agent work status CIS-0001 --task WORK-090 --format json | ConvertFrom-Json
$checkpoint = @{
  expectedRevision = $workStatus.work.revision
  checkpoint = @{
    outcome = "The focused test isolated the unsupported input shape."
    evidence = @("tests/Example.Tests/InputTests.cs:42")
    approach = "Exercise the existing parser through its public service."
    decision = "Keep the existing parser and narrow the correction to that input shape."
    nextAction = "Implement the bounded parser correction and rerun the focused test."
    hypothesis = "The existing parser can represent the required input without another abstraction."
    expectedResult = "The focused case passes and existing parser cases remain compatible."
    materialNewInformation = $true
    closeAttempt = $false
  }
}
$checkpoint | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath .\work-progress.json -Encoding utf8
cis agent work progress CIS-0001 WORK-090 --input .\work-progress.json --actor "Authorized controller" --format json
```

Evidence references are existing results or source locations; do not copy raw logs into the checkpoint or claim an unrun check passed. Known preparation and owned execution are charged from admission to observed stop. Parent/child time is charged once to their shared accounting owner, with child detail retained; independent concurrent roots each consume change capacity. Elapsed wall time is reported separately. This direct route has no suspension protocol or confirmed suspended-user-wait interval. A controller waiting does not make an active reservation or still-running owned execution free. Independently run external commands are outside this accounting coverage.

### Record a disposition and resume

Two consecutive logical attempts with the same blocker and no material new information hold another unchanged expensive repetition. Two expected failing tests during development do not establish that condition. A controller may record a justified in-scope simplification within existing authority and remaining capacity. That decision preserves spent effort and does not require another human prompt where the controller already has the necessary delegation.

Supported disposition kinds are `continue`, `simplify`, `hold`, and `reconcile`. Use a fresh revision, supporting evidence, and an explicit rationale. A bare continuation, reworded hypothesis, or increased allocation does not clear every hold. Hard exhaustion, uncertain recovery, and existing approval or permission gates remain separate.

```powershell
$workStatus = cis agent work status CIS-0001 --task WORK-090 --format json | ConvertFrom-Json
$disposition = @{
  kind = "simplify"
  expectedRevision = $workStatus.work.revision
  hypothesis = "Use the existing parsed input directly instead of introducing another transformation."
  nextAction = "Apply that smaller correction and run the single affected test family."
  evidence = @("tests/Example.Tests/InputTests.cs:42")
}
$disposition | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath .\work-disposition.json -Encoding utf8
cis agent work disposition CIS-0001 WORK-090 --input .\work-disposition.json --actor "Authorized controller" --reason "The focused result supports this smaller approach within the approved scope" --format json
cis agent show RUN_ID --summary --format json
cis agent resume RUN_ID --actor "Authorized controller" --reason "Continue the recorded bounded correction" --message "Apply the recorded next action and retain actual check output" --timeout-seconds 300 --format json
```

Replace `RUN_ID` with the retained terminal run identity. Resume rechecks current policy and remaining capacity; provider-attempt numbering does not create a new logical work allowance. Its optional `--timeout-seconds` chooses a shorter supported bound when the recorded timeout no longer fits; it does not grant capacity. Resupply any explicitly authorized `--allow-command` entries. Use `cancel` and bounded `recover` for the existing owned-process lifecycle, not as an alternative implementation route. A live or uncertain process/reservation must be reconciled before capacity can be safely released.

### Reconcile interrupted preparation

A reservation can outlive interrupted preparation before a run manifest exists. Admission records the original preparing controller's host, process ID, and verified process start time. That execution ownership is separate from the short accounting transaction lock; CIS does not hold the accounting lock throughout preparation or provider execution.

Inspect `work.executions` and identify the exact accounting execution. Reconciliation requires both the 30-second grace period and verifiable evidence that the original preparer stopped. Elapsed time or an absent provider PID alone is insufficient. While the original same-host process identity remains live, the reservation stays protected. An owner on another host, unreadable identity, or an older unresolved record without ownership metadata remains uncertain and held. Let the original invocation finish where possible; a long-lived host can leave an orphan conservatively held until its original process absence can be established. Do not fill in ownership metadata manually or establish a fresh baseline to release it.

Once stopped preparation is established for a recorded never-launched reservation, use `reconcile` with that `executionId`, current revision, existing change/task identity, actor, and rationale. CIS rechecks its immutable ownership record before settlement and conservatively charges at least the full reserved bound rather than assuming no preparation ran. Normal preparation retains its measured cost through its observed end. A finalized reservation cannot subsequently launch a provider. Retiring or moving the task does not prevent reconciliation of its retained accounting identity. A recorded launch intent without a verified process identity remains held; this operation does not treat an unknown launch as never launched.

For example, after stopped preparer ownership has been verified, replace the execution placeholder with the exact never-launched reservation identified by status:

```powershell
$workStatus = cis agent work status CIS-0001 --task WORK-090 --format json | ConvertFrom-Json
$reconciliation = @{
  kind = "reconcile"
  expectedRevision = $workStatus.work.revision
  executionId = "EXACT_EXECUTION_ID_FROM_STATUS"
  evidence = @("Retained preparation evidence for this execution")
}
$reconciliation | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath .\work-reconcile.json -Encoding utf8
cis agent work disposition CIS-0001 WORK-090 --input .\work-reconcile.json --actor "Authorized controller" --reason "Reconcile the identified interrupted preparation at its full reserved cost" --format json
```

When a run manifest exists, use [`cis agent recover`](cis_agent_recover.md) for an absent verified owned process. A terminal manifest with unfinalized accounting can also be reconciled through that operation; its appearance alone does not prove settlement. Recovery preserves a terminal run's recorded outcome, failure kind, and completion time, adds a reconciliation event, and settles its accounting. Live processes, unresolved launch identity, and other unsafe recovery conditions remain visible holds.

## Output and exit codes

All formats expose the work owner, revision, change/task allocations, consumed/reserved/remaining capacity, restoration reserve, attempt usage, coverage, recovery state, hold reason, and next action. JSON retains execution and checkpoint history. `agent show`, run results, and their structured summaries include the available work projection. Status reads do not settle execution or alter consumption.

The result has `status`, `work`, `errors`, `applied`, and `exitCode`. A hold, invalid request, busy accounting transaction, or missing allocation returns nonzero (`4`); an unsupported output format returns `2`. A hold does not imply task completion. Successful allocation or progress does not establish that a provider ran or that product acceptance was granted.

## Related commands

- [`cis agent run`](cis_agent_run.md)
- [`cis agent resume`](cis_agent_resume.md)
- [`cis agent show`](cis_agent_show.md)
- [`cis agent cancel`](cis_agent_cancel.md)
- [`cis agent recover`](cis_agent_recover.md)
