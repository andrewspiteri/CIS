---
title: "Agent Provider Profile"
type: agent-provider-profile
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-06"
review_cadence: "on provider, permission, isolation, or retention-policy change"
cis:
  stable_id: change-impact-studio:reference:agent-provider-profile
---

# Agent provider profile

No direct provider is selected by default. A controller must select a discovered provider for every run. Credentials remain provider-native and must never be copied into CIS configuration, envelopes, events, or canonical documents.

| Setting | Value | Rationale |
|---|---|---|
| default-provider | none | Avoid silent vendor selection. |
| default-transport | provider-preferred | Capability negotiation remains explicit. |
| implementation-isolation | git-worktree | Preserve the user's current working tree while seeding the isolated worktree from its exact tracked and non-ignored untracked baseline. |
| direct-dirty-working-tree | denied | Requires an explicit reviewed policy change. |
| maximum-run-seconds | 3600 | Bound foreground execution. |
| maximum-startup-seconds | 30 | Fail closed when a provider process produces no startup evidence. |
| maximum-idle-seconds | 300 | Classify a silent stalled provider separately from total expiry. |
| approve-within-ceiling | denied | Provider requests require explicit per-run authorization. |

## Providers

| Provider | Enabled | Preferred transport | Allowed modes | Maximum permission | Notes |
|---|---|---|---|---|---|
| codex | yes | app-server | plan, implement, review | workspace-write | `exec-json` is an explicit non-interactive fallback. Resolution checks `CIS_CODEX_EXECUTABLE`, process PATH, then the current Windows Codex Desktop installation. Inconclusive login status is advisory because ambient Desktop/App Server authentication can still execute. Explicit setup uses provider-native browser or device authentication through `cis agent provider authenticate codex`; CIS never receives credentials. Network escalation remains denied. |
| claude | yes | stream-json | plan, implement, review | workspace-write | Headless execution does not relay interactive approvals. Implementation-only `--allow-command` adds literal native rules per attempt; denied commands fail. Native rules are not an OS sandbox. |
| portable | yes | envelope | plan, implement, review | read-only | Preparation only; no direct execution. |

## Authority boundary

Repositories awaiting their first commit are supported. CIS seeds an isolated worktree from the current non-ignored files using a temporary index and a parentless snapshot commit. It does not create a commit on the user's branch or replace their index. Story execution prepares every target worktree before calling an implementation model, so repository setup failures are reported before model work begins.

After an interrupted or failed story execution, retry can reuse successful retained implementations when the approved task, repository baseline and native run evidence still match. Changing selected models does not discard that compatible work or its recorded cost. A fresh reviewer must be independent of every actual retained implementer model identified by the native runs. A changed baseline, invalid evidence or unknown implementer identity holds reuse or review and leaves the retained work available for inspection.

Story execution reviews every linked repository before deciding the round's outcome. Reviewers receive the combined candidate diffs, recorded checks and available work-cost context. Findings state required/optional disposition independently of severity and name their task, policy, decision or invariant basis. Consistent optional advice permits continuation without corrections or mandatory answers; required defects, including minor ones, prevent readiness. Contradictory verdicts are invalid evidence. Required correctable findings use up to two automatic correction rounds retained across governed invocations under the same relationship, each followed by fresh independent review. A relevant authorized disposition can permit one bounded next correction within remaining capacity, preserving prior rounds and work. A genuine required decision, evidence or permission hold remains visible. Human completion is a separate later gate and is never fabricated by either model.

When the actual diff materially expands verification machinery, the existing reviewer records its necessary invariant, adopted native alternative, existing measured/estimated/unknown cost, maintenance/pass-semantics effect and duplication avoided. Ordinary helpers and assertions do not require another reviewer, benchmark or planning artifact.

The task screen refreshes while a run is active and when reopened. It shows the latest work summaries, each round's checks and findings, and whether candidate files were applied. If feature scope or the product baseline changes, the saved plan and its execution history remain visible for inspection, with execution and completion disabled until a current plan is reviewed.

Under **Respond to review findings**, answer only a genuine required finding that needs clarification. Optional advice does not create an answer/save/retry obligation, and a correctable defect normally proceeds through bounded correction. **Suggest answers with selected model** uses task, BRD and recorded execution context to propose answers with supporting source excerpts. Remote suggestions require explicit disclosure permission. Suggestions without matching source excerpts are rejected. **Save answers** records only edited or explicitly selected responses, with identity and the exact material finding; it does not prove a defect fixed or waive required evidence. A changed material review rejects a stale save and retains the editor draft. Incidental run IDs/order do not change equivalent feedback meaning.

Provider availability is not authorization. Direct execution requires accepted impacts, an approved current plan, an eligible task, and a satisfied global design barrier. Agent output is untrusted evidence until explicitly imported; only canonical CIS lifecycle commands and human decisions can approve, accept, transition, verify, or close work.

Direct plan/task runs and governed story execution under applicable adopted engineering defaults (`requireTaskCompletion: true`) or authority-owned accounting require an authorized containing-change/task allocation through [`cis agent work`](../manual/cis_agent_work.md). Story `work-status` exposes the existing owner; `bind-work --input` can establish a missing association to current approved canonical work without granting allocation. An exact registered owned participant's controls permit allocation at its authority. Retained story correction history also permits explicit finite allocation against existing approved canonical work.

Establishing that allocation adopts work accounting across the authority. Every supported direct-task and story route then needs its canonical owner and applicable finite allocation, including sibling tasks without correction history or engineering quality policy. Allocating one task grants no capacity to another. This does not adopt engineering quality policy or grant plan, design or completion approval. Implementation, review, correction, retry and owned-repository stages share consumed time, reservations, restoration capacity and logical attempts. A missing allocation is a specific hold; missing/corrupt adopted accounting requires reconciliation. The profile alone does not adopt controls. Authorities without adopted accounting or applicable engineering defaults retain their non-adopted execution boundaries. Product-document authoring and import operations retain their dedicated flows.

The maximum foreground bound remains 3,600 seconds; allocation and remaining capacity may require a shorter bound. A justified in-scope simplification can proceed under existing delegation, while hard exhaustion, recovery uncertainty and real approval gates remain separate. Coverage is limited to supported controllers sharing the authoritative state, not independent hosts or raw external agent commands.

Associated native mutation/incremental execution inherits the live work owner through CIS-created environment context, validated against durable authority and process ownership. Standalone work uses an existing approved change/task through `--change`, `--task`, `--actor` and, for a linked owned target, `--authority`. Preparation, actual native launch, result admission and recovery share inclusive time and logical-attempt accounting. Profile/run changes cannot refill capacity. A missing allocation or finite numeric owned-storage policy holds before costly preparation; profile timeouts and per-run byte caps never supply that authority.

Native mutation expansion requires current representative evidence for the relevant runner/adapter/fixture/configuration path: healthy baseline, detected mutation, surviving control, rejected infrastructure failure, restored health and any claimed native child transport. The controller records its actual outcome and approach through existing `agent work progress` before broadening. A native report's validity and threshold remain separate from qualification; incomplete scope, restoration, cleanup, accounting and storage holds cannot become passes. Compatible material evidence remains reusable across accounting-only changes. Non-adopted diagnostics label their unaccounted coverage.

Native and artifact operations reserve growth against the allocation's finite owned-storage policy, including declared outputs/scratch and associated CIS agent/archive/retrieval data. Physical overlap is deduplicated; separate copies remain counted. Cleanup uses existing plan review, delegation and confirmation; counting a path grants no deletion authority. Enforcement covers supported native/artifact boundaries with bounded sampling, not arbitrary provider writes or instantaneous machine-wide storage. Explicit `--recovery-of` performs only a retained execution's unchanged recovery recipe and healthy postflight within its existing reserve, preserving its original attempt and all independent holds.

When story reflection is due, `story work-status` reports `controller-assessment-required`. The authorized orchestrating agent inspects retained implementation, failed/valid review evidence, measured cost, reservations, remaining capacity and current revision. It uses `cis agent work progress <change-id> <task-id> --input <assessment.json> --actor <controller>` at the existing canonical owner to record Outcome, nonempty Evidence, Approach, Decision and the next bounded action; CIS supplies measured Cost. Keep `closeAttempt: false` unless the logical attempt has ended, then resume the governed story. No routine human acknowledgment or extra reviewer is required. Automatic `story-execution-summary` and legacy `story-controller-progress` records never qualify, and candidate reuse cannot refresh reflection credit. Explicit controller progress remains the owner-wide assessment route; incidental story summaries cannot satisfy direct-task reflection. Status reads remain non-mutating, and all independent holds and counters remain binding.

Granted corrections deliver their authorized hypothesis, specific action and disposition provenance to the provider and independent reviewer. Recovery and extensions retain prior direction, round identity and spent work; missing legacy direction requires exact-disposition recovery or the reported bounded `simplify` reconciliation. Nested stages created by the governed story or correction controller may use the admitted parent's positive whole-second remainder below 30, rounded down within parent/correction deadlines; preparation stays charged. Direct `cis agent run` requests, including `--parent-execution`, and new root requests retain the 30-second minimum; direct requests must fit the parent's remainder without automatic shortening. An early retry failure preserves string-only legacy feedback and still-current response provenance until a new material review or task/source change. A valid newer empty structured review supersedes obsolete findings; answers never waive required defects or approve completion.

See [agent run](../manual/cis_agent_run.md#explicit-command-permissions) for command-rule syntax and native matching limits. Command lists are recorded with actor/attempt and must be supplied again on resume; native session context restarts when either attempt uses a list. Availability and a model success message are not proof that required commands ran.
