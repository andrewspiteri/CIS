---
title: "cis agent work"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-11"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-work
---

# `cis agent work`

Allocate the work allowed for a change and its logical tasks, inspect consumption, and record a bounded continuation through the headless CLI. Starting a new run, changing providers, or resuming a run does not restore spent work.

## Reuse outcome authority

Read the shared authorization recorded by [`cis change authorize`](cis_change_create.md#authorize-the-outcome-once).
Where it delegates resource distribution, its controller allocates task capacity
within the same overall time, attempt and storage limits using the existing
`agent work allocate` command. This is a controller distribution under the original
human decision; each new or split task needs no separate human approval.

The request binds the current authorization and ledger revision. Shared storage and
decision references may come from that authorization. Keep honest task ownership,
consumption, reservations, restoration reserves and attempt history. Adding tasks or
switching agents/profiles never resets costs or creates another containing allowance.
An actual overall-limit increase requires its specific new human decision; unrelated
work remains eligible within its own dependencies and remaining shared capacity.
Legacy fixed-task allocations retain their original limits and amendment rules.

With delegated outcome distribution, CIS uses the authorized containing limits and
controllers and treats task entries as redistributable distributions. The sum of
task capacity must still fit that shared allowance after protected costs and
reservations. Increasing a task distribution within it is controller work; increasing
the overall grant requires an amended outcome decision before allocation.

## Scope and authority

Direct `agent run`, direct-task `agent resume` and governed story stages require work accounting under applicable outcome authority, adopted engineering defaults (`requireTaskCompletion: true`) or existing authority-owned accounting. Story control applies when the authority or a linked owned target has adopted it; an unconfigured target does not exempt the shared work. An exact registered owned participant's controls permit allocation at an authority that has no engineering quality policy. Retained story correction history also permits an authorized finite allocation against existing approved canonical work. Without outcome authority or another adopted route, a fresh minimal authority can explicitly adopt accounting alone with `agent work allocate --adopt-accounting`, using that same approved canonical ownership and an authorized finite allocation.

Establishing that allocation adopts work accounting across the authority: every supported direct-task and story route then needs its canonical owner and applicable finite allocation, including sibling tasks with no correction history or engineering quality policy. Allocating one task grants no capacity to another. This adopts work accounting, not engineering quality policy, and grants no plan, design or completion approval. A provider profile alone does not adopt accounting. Authorities without adopted accounting or applicable engineering defaults retain their existing non-adopted execution boundaries. Product-document authoring and import operations retain their dedicated flows.

The controller supplies an allocation from the existing authorized planning or controller process. The actor and decision reference record that authority; provider output, a resume reason, and `--approve-requests` do not grant it. An absent allocation on a governed route holds execution and identifies the missing allocation decision. Allocating work does not approve a plan, accept impacts, satisfy a design barrier, or transition a task.

One authority-owned record retains the containing change, logical accounting owners, allocations, measured consumption, reservations, restoration reserve, logical attempts, checkpoints, and recovery uncertainty. CIS diagnostic cleanup protects `.cis/local/agents/work-control.json`, `work-control-adoption.json`, and `work-control.lock` under that same directory. Those three accounting, adoption, and transaction-lock files are distinct from disposable run diagnostics even though the existing agents retention family covers their parent directory. Raw external deletion is outside that cleanup guarantee. Do not edit or delete these files to reset work: missing or corrupt accounting after adoption holds execution and requires reconciliation, not a new empty allocation.

Historical runs remain readable. Initial allocation must explicitly acknowledge that earlier unmeasured effort is unknown; it does not assert that those runs cost nothing. The accounting guarantee covers supported controllers sharing this authoritative state. It does not supervise independent hosts or raw external agent commands. Storage owner and ceiling references are recorded; complete storage enforcement is outside this slice.

Supported mutations compact redundant settled execution and checkpoint history
before it consumes the finite ledger allowance. Aggregate consumption, used attempts,
allocations and open/closed attempt state remain authoritative. Unresolved work,
current required execution references and their dependencies remain available,
alongside bounded recent detail and necessary compact history summaries. Compaction
grants no capacity and cannot reopen a closed attempt. Status reads do not rewrite
the ledger. Irreducible required state can still hold admission safely.

Current cache metadata retains the exact native work references it still needs,
even when a later metadata operation publishes it. A successful replacement updates
those dependencies; failed publication keeps the previous set. When recovery closes
an interrupted publication, it preserves both the existing and candidate dependencies
under the original claim until a later successful publication replaces them.
Compaction preserves current and pending references while removing unrelated settled history.

### Story ownership and continuation

Inspect a story task without allocating or launching work:

```text
cis brd feature wizard story work-status --workspace <authority-path> --slug <feature> --story <story-id> --task <task-id> --format json
```

Reuse its existing unambiguous canonical association. If the association is missing, prepare and inspect the existing approved change/task, then use `cis brd feature wizard story bind-work --workspace <authority-path> --input <binding.json> --format json`. The binding request contains `slug`, `storyId`, `taskId`, current `expectedPlanHash` and `expectedRevision`, `changeId`, `workOwnerTaskId`, `actor` and `reason`. Get the story tokens from current story status. Binding records an association to existing approved work; it neither allocates capacity nor alters the approved implementation contract. Genuine ambiguity remains a targeted hold.

Use that canonical change/task with the allocation, progress and disposition commands below. When entering finite accounting from correction-only history, first allocate with the explicit acknowledgment of unknown historical effort, then follow the displayed owner-scoped `reconcile-story-history` action before reuse or a specific correction grant. These operations grant no plan, design or completion approval. Implementation, independent review, grouped corrections, retries and linked owned repositories share this authority ledger and logical attempt. Stage IDs and provider attempts retain detail without creating fresh owners. Admission precedes costly preparation; compatible candidate reuse preserves historical cost and charges only newly performed work. Generic `agent resume` redirects story stages to the governed story `execution-plan` / `execute` route.

The two automatic correction rounds are durable under the story relationship. A round groups required findings across the relevant candidate set; it is not each reviewer, repository, command or failing development test. Optional advice remains visible without compulsory work or answers. At the limit, inspect current correction history and required finding references before recording a relevant authorized bounded disposition. Review clarification and correction share the same capacity and independent holds.

## Commands

```text
cis agent work allocate <change-id> --input <allocation.json> --actor <identity> --reason <rationale>
cis agent work allocate <change-id> --adopt-accounting --input <finite-allocation.json> --actor <allocation-authority> --reason <decision-rationale>
cis agent work amend-time <change-id> <owner-task-id> --input <amendment.json> --actor <original-authority> --reason <rationale>
cis agent work status <change-id> [--task <task-id>]
cis agent work progress <change-id> <task-id> --input <checkpoint.json> --actor <identity>
cis agent work disposition <change-id> <task-id> --input <disposition.json> --actor <identity> --reason <rationale>
```

These commands accept `--repo <authority-path>` and `--format <human|json|agent>`. `status` is read-only. The other operations check current authority and the input revision before changing work state. Request files contain UTF-8 JSON; use the current `work.revision` from status for an update rather than modifying internal accounting files.

### Allocate

Allocation values describe active time in seconds and logical-attempt limits. CIS reports measured capacity in milliseconds. A logical attempt is a bounded hypothesis investigation or planned delivery step; a provider invocation, command, or expected failing development test is not automatically a new logical attempt.

Use `--adopt-accounting` for explicitly authorized first use when no applicable adopted controls or retained correction history exists. The request must include finite `storage.maximumBytes`, explicit owned `storage.roots` and any restoration reserve, as well as the ordinary task/change bounds, actor/reason, decision references and historical-unknown acknowledgment. A storage reference alone is insufficient for this route. The flag is a command option, not an allocation-JSON property. It does not approve a plan, accept impacts, make a task eligible or supply allocation authority.

This adopts accounting across supported direct, story and native routes in the authority: sibling tasks need their own canonical ownership and capacity, or legitimate inherited capacity. It does not enable `requireTaskCompletion`, completion thresholds or another engineering-quality policy. Once accounting exists, omit the first-use option on ordinary updates; repeating it cannot reset consumption, history, reservations, original limits, uncertainty or revision checks. Missing/corrupt adopted state and historical governed-run references still require restoration/reconciliation through existing controls. The same ledger, adoption reference and transaction lock are used; no cross-file atomicity guarantee is added.

The first allocation supplies:

| Field | Meaning |
| --- | --- |
| `changeSeconds`, `changeAttempts` | Authorized containing-change allowance. |
| `restorationSeconds` | Capacity retained for necessary restoration. |
| `tasks` | Existing task identities and their `seconds`, `attempts`, and `restorationSeconds`. The reserve defaults to zero only when that does not reduce an existing reserve. |
| `tasks[].fixed` | A separately fixed task limit; defaults to `true`. |
| `tasks[].allowRedistribution` | Whether remaining task allocation may be redistributed within delegated authority; defaults to `false`. |
| `decisionReference` | Existing policy or controller allocation decision. |
| `storageCeilingReference` | Existing storage policy/decision reference shared by the containing work. Native and artifact expansion also require its finite `storage` policy. |
| `storage` | Finite `maximumBytes`, declared owned `roots`, optional `restorationBytes` within that ceiling, and delegated `cleanupControllers`. It is allocation authority, not a test-profile limit. |
| `controllers` | Identities delegated to operate this allocation. |
| `authorizationId`, `authorizationDigest` | Current shared outcome binding when applicable; omitted values are filled from its canonical record. |
| `acknowledgeHistoricalUnknown` | Explicit acknowledgment required for the initial unmeasured historical baseline. |
| `expectedRevision` | Omit on initial allocation; supply the current revision on updates. |

Updates preserve consumption, reservations, restoration capacity, history, and applicable fixed limits. Delegated redistribution must fit the authorized outer allowance. Under legacy fixed-task authority, only the original allocation authority may grant an eligible new root task capacity from unallocated time and logical attempts without altering existing fixed caps. Under delegated outcome authority, the authorized controller distributes that capacity. Neither route clears another owner's holds. A new filename, task decomposition, provider, profile, or source fingerprint cannot create another allowance for the same containing work.

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

A nested stage inside an admitted story or correction window may use the parent's remaining whole seconds, including fewer than 30. CIS rounds down and keeps the child within both the parent deadline and any correction cap; preparation stays charged. Less than one usable second holds before provider launch. This does not change the 30-second minimum for a new root request, adopted ceilings or restoration reserves.

Recovery uncertainty holds conflicting work for the affected accounting owner. Other independent owners may proceed when their own authority and holds permit it and the remaining shared change capacity still fits. The uncertain reservation continues to count against that shared allowance.

### Amend an existing task's time ceiling

Use `amend-time` only for an explicit new decision from the original allocation
authority. Delegated controller access alone is insufficient. Supply the existing
accounting owner, a fresh revision from `status`, the absolute replacement ceiling
in seconds, the new decision reference and a reason. The operation raises task
time only; ordinary `allocate` continues to preserve fixed limits.

This synthetic request illustrates the input shape and grants no authority:

```json
{
  "seconds": 450,
  "decisionReference": "decision:approved-task-time-amendment-example",
  "expectedRevision": "<current-work-revision>"
}
```

```text
cis agent work amend-time CIS-0042 WORK-120 --input amendment.json --actor "Original allocation authority" --reason "Apply the explicit task-time decision" --repo <authority-path> --format json
```

An authorized amendment may raise the prior task allocation pool. It does not
increase the containing-change ceiling: admission and settlement still enforce
both task and change capacity, including their protected restoration reserves.
It preserves consumption, reservations, attempts, bindings, storage policy,
fixed/redistribution flags, independent holds and historical decisions. It does
not admit work or start another logical attempt. Repeating a request cannot add
the same increase again.

The result exposes the effective task ceiling, ordinary remaining capacity,
current revision and task-time amendment history. The original allocation decision
remains intact; each amendment records its original authority, reason, new decision
reference, prior/new ceiling and revision provenance. Recheck status and all other
execution gates before running the next bounded action.

### Record progress

Record reflection at the first useful result, before broader work, and within the adopted execution window of cumulative active work. The reviewed provider profile sets that window to 3,600 seconds; an applicable narrower window governs instead. CIS checks at the next safe admission boundary, rather than interrupting a healthy operation or starting a timer for each command. Once cumulative measured work since the last qualifying controller checkpoint reaches the window, another expensive run or resume returns a non-success hold directing the controller to `agent work progress`.

This is routine execution work, not another approval or reviewer call. A valid controller checkpoint can keep the same logical attempt open with `closeAttempt: false`; it does not transition the canonical task to `Blocked`. New run IDs, provider changes and resumed provider-attempt numbers do not reset the window or spent work. Automatic settlement summaries, disposition summaries, status reads and model success claims do not qualify as controller reflection. Existing controller records are reused only when their provenance and required content identify them unambiguously.

One qualifying checkpoint comes through the controller progress operation and records Outcome, nonempty Evidence, Approach, Decision, and the next bounded action. CIS records its origin and measured Cost context; the checkpoint cannot overwrite consumption. Optional `hypothesis` and `expectedResult` identify the investigation. Set `closeAttempt` only when that logical attempt has concluded. A `blockerKey` identifies a repeated blocker; `materialNewInformation` records the controller's judgment rather than asking a semantic classifier to infer progress. Recording reflection permits continuation automatically when all other gates allow it; it does not clear repeated-blocker, allocation, recovery, permission or required-review holds.

When story reflection is due, `story work-status` reports `controller-assessment-required` and the supported next command. The already-authorized orchestrating agent inspects retained implementation, failed or valid review evidence, measured consumption, reservations and remaining capacity. It then uses `cis agent work progress <change-id> <task-id> --input <assessment.json> --actor <controller>` at the existing canonical owner with the current revision. Record Outcome, nonempty Evidence, Approach, Decision and the next bounded action; CIS supplies measured Cost. Keep `closeAttempt: false` unless the logical attempt has actually concluded, then resume the same governed story with refreshed tokens. Routine assessment needs no human acknowledgment, extra reviewer or canonical task `Blocked` transition.

Automatic `story-execution-summary` records, including candidate-reuse and failed-review summaries, preserve observations but never qualify as controller assessment. Legacy `story-controller-progress` records do not qualify either. Another run ID or reuse of the same implementation does not refresh reflection credit. Explicit authorized controller progress remains the existing owner-wide assessment route; incidental story summaries cannot satisfy direct-task reflection. Status reads remain non-mutating, and repeated-blocker, correction, hard-capacity, recovery, permission and approval holds retain their own requirements.

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

Supported disposition kinds are `continue`, `simplify`, `hold`, `reconcile`, and `reconcile-story-history`. Use a fresh revision, supporting evidence, and an explicit rationale. A bare continuation, reworded hypothesis, or increased allocation does not clear every hold. Hard exhaustion, uncertain recovery, and existing approval or permission gates remain separate.

For a story correction-limit hold, a relevant `simplify` disposition also identifies the retained `correctionRelationshipId`, current `correctionFindingReferences`, and `correctionMaximumSeconds` for one bounded next step. Use the relationship and references returned by status; do not invent them. The bound must fit existing authority and remaining capacity. Prior rounds remain recorded, an interrupted started round remains charged/retained, and a grant is not two fresh rounds. Routine reflection without a correction-limit hold still uses the ordinary progress/disposition fields; it needs no additional human approval when existing delegation suffices.

The pending continuation's `authorizedDirection` and the round's ordered `authorizedDirections` retain the controller's hypothesis, specific next action, actor, time, reason, evidence and disposition reference. The applicable direction reaches the correction provider and subsequent independent reviewer. Recreation, interrupted-round recovery and a valid extension retain that direction, prior directions, compatible completed stages, round identity and spent work. An extension supplies the applicable new direction; it does not waive findings, grant permissions or reset allowance.

For a pre-fix pending grant or granted round missing copied direction, CIS recovers only the exact retained authorized disposition. If it cannot establish that direction, status names the missing reference and holds execution. Restore verified retained disposition evidence where available. Otherwise use the same `simplify` route with current revision, exact relationship/findings, changed hypothesis, specific `nextAction`, evidence and a finite `correctionMaximumSeconds`. A safely finalized started round can reconcile future direction within its usable remaining cap, or receive an eligible bounded extension when exhausted. Unrecoverable prior direction remains explicitly unknown; provider/review prose or a generic continuation cannot supply it. Ordinary automatic rounds that required no disposition retain their existing behavior.

In genuinely non-adopted correction-only work, another actor may claim the next numbered round for the same relationship and identity only when none is started, preserving counts and the two-round automatic limit; started work stays with its original controller or requires explicit canonical binding, authority-wide finite accounting and supported history reconciliation, never a ledger edit or actor rename.

An exhausted pending grant, before its correction round has started, can receive a replacement `simplify` disposition with a fresh revision, the same relationship and current required findings, a changed justified hypothesis, evidence, `nextAction` and a finite `correctionMaximumSeconds` of 30–3,600 seconds. The replacement preserves previously spent owner work and correction history; task/change allowances and other holds remain binding. Once the round has started, use the same-round extension below instead.

If an already granted, started round has fewer than 30 usable seconds left, an authorized controller can use the same `simplify` request to authorize one further bounded step. Supply a fresh revision, the same relationship and current required-finding references, a changed justified hypothesis, evidence and `nextAction`. Here `correctionMaximumSeconds` is the additional step of 30–3,600 seconds. Every associated owned root and child must already be safely finalized without recovery uncertainty. CIS retains the same round ID, number, material context and completed stages, and records the extension reference in that round's `continuationReferences` and the owner checkpoint. Its cumulative cap includes all measured round work plus the newly authorized step; consumption, logical attempts, restoration reserves, task/change limits and provider ceilings remain unchanged. Retry or containing-task reallocation alone does not extend this cap.

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

### Reconcile historical story effort

When adopted story status identifies retained unmeasured runs, establish the authorized canonical allocation and its historical-unknown acknowledgment first. Then use `reconcile-story-history` on that same canonical change/task. Copy the exact `correctionRelationshipId`, retained `historicalRunIds` and current revision from the reported hold/status; do not infer identities or edit accounting files.

This synthetic request shows the field names. Replace every placeholder with the current service-reported value:

```json
{
  "kind": "reconcile-story-history",
  "expectedRevision": "CURRENT_WORK_REVISION",
  "correctionRelationshipId": "EXACT_STORY_RELATIONSHIP",
  "historicalRunIds": ["EXACT_RETAINED_RUN_ID"]
}
```

```text
cis agent work disposition <change-id> <work-owner-task-id> --repo <authority-path> --input <historical-reconciliation.json> --actor <authorized-controller> --reason <rationale> --format json
```

CIS verifies the exact terminal native story records, artifact integrity, timing bounds and stopped owned processes. Live or uncertain ownership remains held. Reconciliation conservatively charges at least each retained run's recorded bound, once, without erasing prior correction rounds or transferring a charge to another owner. A repeated identical reconciliation is idempotent. Initial historical acknowledgment alone does not price those runs or make their effort zero. Once this hold is resolved, governed story continuation may reuse compatible implementation evidence while charging only newly performed work.

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

When a run manifest exists, use [`cis agent recover`](cis_agent_recover.md) only when all relevant owned identities are resolved and verified stopped. A terminal manifest with unfinalized accounting can also be reconciled through that operation; its appearance alone does not prove settlement. [`cis agent cancel`](cis_agent_cancel.md) can stop independently verified live owned processes even while another identity remains unresolved; unverifiable processes stay untargeted. Partial cleanup returns non-success and retains the reservation and conflicting-work hold. Once settlement is safe, recovery preserves a terminal run's recorded outcome, failure kind, and completion time, adds a reconciliation event, and settles its accounting once. Live processes, unresolved launch identity, and other unsafe recovery conditions remain visible holds.

## Native verification and owned storage

`cis test mutation run` and `cis test incremental run` use the same owner for preparation, native processes, result admission and required recovery. Supply `--change`, `--task` and `--actor` when no unambiguous approved binding is available; `--authority` identifies a linked owned repository's authority. A live governed parent is inherited through CIS-created context and verified against durable process ownership. `--parent-execution` names an existing parent; an ID or provider-written result cannot grant capacity. Read-only planning, status and evidence inspection do not launch native work.

The command's `--max-seconds` and native profile limits only tighten the existing allocation. They do not allocate work. Child stages use their parent's positive remaining whole seconds, including fewer than 30, with preparation and launch bookkeeping charged inside that window. Normal roots retain the 30-second minimum. Nested active intervals count once; a new profile, batch or run ID does not create a logical attempt or reset costs. Genuinely non-adopted diagnostics report unaccounted coverage and cannot establish governed accounting.

Native mutation expansion also needs current representative correctness evidence and an authorized controller's approach checkpoint. Inspect the actual baseline, detected mutation, survivor control, infrastructure rejection, restored health, native process observations and measured cost. Record the displayed `native-pilot:<basis>` evidence reference through `agent work progress` with Outcome, Approach, Decision and the next bounded action. This is the existing controller assessment, not another human acknowledgment or reviewer. Automatic summaries cannot supply it. A passing score is separate from valid evidence and readiness; recovery, required scope and storage holds remain binding.

Allocate storage once at the owning boundary. The following fragment is **synthetic** and supplies no production authority:

```json
{
  "storage": {
    "maximumBytes": 1073741824,
    "restorationBytes": 67108864,
    "roots": [{"repositoryPath": "<authority-path>", "path": ".cis/local"}],
    "cleanupControllers": ["Authorized controller"],
    "allowNativeScratch": true
  }
}
```

Owned totals include declared physical paths, known CIS agent/management data and exact associated native scratch. Overlapping paths count once; separate copies, ZIPs, manifests and retrievals still count. Count-only roots are protected and grant no deletion permission. Native and artifact writers reserve growth before expansion, then reconcile surviving bytes. Existing per-run limits/free-space checks also apply. Monitoring is bounded and sampled; it is not a machine-wide filesystem sandbox. Inspect `artifacts inventory` and review `artifacts plan` before delegated `--yes` cleanup. Deletion frees measured bytes, never time or attempts; protected/current/recovery/accounting and user-managed material remain protected.

When native restoration is required, resume its retained profile. `--recovery-of <execution-id>` performs only the original pinned recovery command and healthy postflight, including when an interrupted controller lost its ordinary result record. The exact owner, original attempt and recovery recipe remain bound. Safely stopped recovery can spend the existing protected restoration reserve; it cannot clear live/uncertain process ownership, change its command or run a new campaign. Missing reports remain missing. Ordinary archive retrieval/restoration is a new bounded operation and does not acquire native recovery authority.

Exact outside-root native scratch retains its admitting path, temporary parent and
canonical native execution in the existing claim. Later TEMP changes neither expand
ownership nor invalidate that binding. Historical claims need the same exact path in
retained storage admission and operation paths, with its owner and native execution;
missing or ambiguous provenance holds inspection/reconciliation without changing
claims or reservations. Reads never install provenance from the current TEMP.
Existing exact-name, reparse, finite-capacity and stopped-owner checks still apply.

An exact, otherwise authorized restoration may proceed through a repeated-blocker
exploration hold. The hold and blocker history remain in place for later diagnostic
work; recovery retains the original attempt, consumption and reserve. Process
uncertainty, cancellation, other owner holds, permissions and hard limits still apply.

For an owned native cancellation, use `agent work disposition` with `kind: "cancel-native"`, the exact `executionId`, current revision, actor and reason. CIS targets only verified recorded identities. Unknown processes, the cancellation caller's own ancestry and other hosts are not targeted. Partial cleanup returns a hold; cancelling a driver alone does not prove every native child stopped. Inspect retained native recovery evidence and use the existing exact stopped-execution reconciliation route before capacity is released. Read-only diagnosis and cancellation remain available when normal expansion is held.

For supported Windows native work, `kind: "reconcile"` verifies the original
preparer and driver identities plus the recorded outer process job in its original
Windows named-object namespace and session. New receipts capture `WindowsObjectPath`
from the actual created job handle. An opened job must match that exact path; a
missing local name proves destruction only after a bounded empty job created through
the same API establishes an exact namespace match. Session equality alone is not
namespace proof: Windows also isolates named objects by application context.
An unreadable job or namespace, reused identity, another host/session or historical
unbound launch remains held.

On Linux, the existing runner starts its trusted native worker in a separate
session. Before releasing that worker's launch gate, CIS verifies and records the
actual session leader's process identity under the recorded driver. Cancellation
uses that retained session to stop its live process groups, then targets the
verified driver and controller identities. Reconciliation requires the recorded
owners and driver to have stopped and the retained session to have no live
members. It does not infer child closure from the driver's absence. This covers
the supported trusted runner; it does not contain code that deliberately detaches
into another session. A missing or ambiguous historical session binding stays
unknown. Use controlled unit retirement below when its conditions apply.

Successful reconciliation charges observed execution time when available, or the
original admitted bound when duration is unknown, and releases only pending
work/storage reservations, preserving
interrupted diagnostics and the original restoration obligation. The unchanged
`--recovery-of` route can then use the remaining protected time and storage reserve;
reconciliation grants no new allowance or attempt.

Historical receipts without the original object path remain held. Recover that
binding only from genuine retained launch evidence or a verifiable original context;
do not fill it from the current caller, job GUID or an absent PID. Cancellation and
human authorization cannot replace factual containment proof. See Microsoft's
[named-object isolation](https://learn.microsoft.com/en-us/windows/uwp/communication/sharing-named-objects)
and [job-object lifetime](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
contracts. Unsupported native object-path queries also hold rather than infer closure.

If native execution finished safely but a storage settlement failed, use that same
`reconcile` disposition with the original execution ID and current revision. CIS
verifies the stopped subtree and settles its pending storage reservations while
protecting their claimed paths. It does not charge finalized execution time again
or run the native command again. Missing process proof or unresolved children
remain held; reconciliation cannot waive provenance or native correctness checks.

### Retry interrupted disposable unit work

An interrupted unit invocation can be retired as inconclusive after its processes
are cleaned up or its disposable execution environment is retired. Use
`abandon-native-unit` on the exact original root with the fresh work revision,
authorized controller, reason and factual cleanup/retirement evidence. This follows
the existing native invocation lock and accounting; it does not require historical
machine custody, boot attestation or a mutation recovery command.

For example, after actually retiring the old disposable executor and its shared
resources, supply this input to `agent work disposition <change> <task>`:

```json
{
  "kind": "abandon-native-unit",
  "expectedRevision": "EXACT_CURRENT_REVISION",
  "executionId": "EXACT_ORIGINAL_UNIT_ROOT",
  "disposalBasis": "retired-environment",
  "evidence": ["Actual retirement observation for that disposable executor and its resources"]
}
```

Selecting a basis does not perform cleanup. The controller must first establish
that old work cannot interfere. `process-cleanup` uses the supported complete
process/job or Linux-session cleanup checks; `retired-environment` records the controller's factual
retirement of the disposable executor. A known live driver or another active
invocation still holds disposal. A long-lived controller can retire its own
returned invocation without exiting. Missing historical fields stay unknown.
Mutation or restoration work cannot use this unit-only disposition.

Save the populated input as `unit-disposal.json`. Supply the actual reason through
the command's `--reason` option:

```text
cis agent work disposition <change> <task> --input unit-disposal.json --actor "Authorized controller" --reason "Retired the identified executor and its shared resources" --format json
```

Successful disposal reports `abandoned`. It keeps the old invocation interrupted
and inconclusive, closes its logical attempt without refunding it, settles pending
accounting and preserves valid completed children and useful diagnostics. Unknown
execution duration charges its admitted bound; an observed stop or real overrun is
retained. Reconciliation waiting is not execution time. Disposal neither creates a
test pass nor grants another allowance. Inspect the remaining time, attempts and
storage, make any required supported task transition, then retry the ordinary
unit-only invocation. The existing lease checks exact saved references, clears
settled activity markers and runs only unfinished or invalidated work.

Read-only storage inspection follows retained ownership even when the task is
Blocked, retired or otherwise ineligible for another launch. That read does not
change task state or grant execution. A failed first ledger transaction also does
not discard an already observed native stop: finalization retries that same
observation a bounded number of times.

### Qualify a proposed restart recovery

`prepare-restart-closure` and `verify-restart-closure` are read-only preflight
dispositions. They identify the exact retained native root and its whole tree, then
report why restart closure cannot currently qualify. **There is no positive restart
recovery verifier in this version.** Neither operation prepares a challenge or
establishes that any process stopped. Do not restart a machine on the strength of
their output.

Supply the original authority, change/task, exact root `executionId`, fresh
`expectedRevision`, authorized controller and reason through the existing
`agent work disposition` command. The input uses the ordinary disposition schema:

```json
{
  "kind": "prepare-restart-closure",
  "expectedRevision": "EXACT_CURRENT_REVISION",
  "executionId": "EXACT_NATIVE_ROOT_FROM_STATUS"
}
```

Use `verify-restart-closure` to inspect the verification prerequisites. A valid
request returns `held`, exit `4`, `applied: false`, and `nativeRestartClosure` with
`state: "not-qualified"`, the adoption, revision, root, execution count, diagnostic
tree digest and stable gap codes. `original-host-custody-unproven` means the retained
launch records do not prove continuity from the original host;
`anti-copy-rollback-unqualified` and `restart-verifier-unqualified` identify the
remaining unsupported proof mechanisms. Verification also reports
`challenge-not-prepared`. The digest covers the raw retained execution records and
authority/adoption/revision. It does not hash artifact file contents and is neither
a machine attestation nor a reusable closure receipt. Caller `evidence` is context
only. Unknown proof or challenge fields reject under the existing strict schema.

A hostname, absent or reused PID, session, uptime change, clock change or freshly
created machine-scoped seal cannot retroactively establish the original host.
Microsoft documents [DPAPI machine scope](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
and [uptime independent of clock adjustments](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64);
neither supplies historical custody or anti-clone attestation. Copied machine state
and ambiguous VM rollback history remain insufficient. These operations therefore
hold on every platform, including Windows, without probing original processes or
boot state. Implementing an acceptable original-custody and restart verifier would
require separately qualified evidence and further implementation.

Preflight leaves the ledger and adoption records byte-identical. It does not change
historical `WindowsObjectPath`, reservations, consumption, limits, attempts,
restoration, evidence or task status. Existing admission holds and namespace-based
reconciliation are unchanged. Missing reports remain missing; human authorization
cannot fill the factual proof gap.

## Output and exit codes

All formats expose the work owner, revision, change/task allocations, consumed/reserved/remaining capacity, restoration reserve, attempt usage, coverage, recovery state, hold reason, and next action. JSON retains execution and checkpoint history. `agent show`, run results, and their structured summaries include the available work projection. Status reads do not settle execution or alter consumption.

In correction history, a granted round's `startedAtUtc` is its grant measurement origin, including active preparation performed before the candidate was verified and the round was created. It is not that later verification or creation time. Measured correction cost is the union of relevant active intervals from that origin; settled waiting between invocations is excluded, even when the timestamp is much earlier than the latest continuation.

The result has `status`, `work`, `errors`, `applied`, and `exitCode`. A hold, invalid request, busy accounting transaction, or missing allocation returns nonzero (`4`); an unsupported output format returns `2`. A hold does not imply task completion. Successful allocation or progress does not establish that a provider ran or that product acceptance was granted.

## Related commands

- [`cis agent run`](cis_agent_run.md)
- [`cis agent resume`](cis_agent_resume.md)
- [`cis agent show`](cis_agent_show.md)
- [`cis agent cancel`](cis_agent_cancel.md)
- [`cis agent recover`](cis_agent_recover.md)
