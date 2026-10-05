---
title: "Agent Provider Profile"
type: agent-provider-profile
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
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
| claude | yes | stream-json | plan, implement, review | workspace-write | Headless execution uses predeclared permissions. |
| portable | yes | envelope | plan, implement, review | read-only | Preparation only; no direct execution. |

## Authority boundary

Repositories awaiting their first commit are supported. CIS seeds an isolated worktree from the current non-ignored files using a temporary index and a parentless snapshot commit. It does not create a commit on the user's branch or replace their index. Story execution prepares every target worktree before calling an implementation model, so repository setup failures are reported before model work begins.

After an interrupted or failed story execution, retry can reuse successful retained implementations when the approved task, selected models, repository baseline and run evidence still match. Reused files receive a fresh independent review before application. A changed baseline or invalid evidence blocks reuse and leaves the retained work available for inspection.

Story execution reviews every linked repository before deciding the round's outcome. Reviewers receive the combined candidate diffs and recorded checks. Correctable findings trigger up to two automatic correction rounds with the selected implementation model, each followed by a fresh independent review. Each round preserves its earlier working copies and run evidence. A blocked review stops for the missing decision, evidence or permission; unresolved findings after the second correction remain visible and no changes are applied. Human completion is a later, separate gate and is never fabricated by either model.

The task screen refreshes while a run is active and when reopened. It shows the latest work summaries, each round's checks and findings, and whether candidate files were applied. If feature scope or the product baseline changes, the saved plan and its execution history remain visible for inspection, with execution and completion disabled until a current plan is reviewed.

Under **Respond to review findings**, enter an answer for each finding that needs clarification. **Suggest answers with selected model** uses task, BRD and recorded execution context to propose answers with supporting source excerpts. Remote suggestions require explicit disclosure permission. Suggestions without matching source excerpts are rejected; an unanswered finding can remain without a suggestion. **Use suggested answer** copies one suggestion into your editable draft. **Save answers** records only your edited or explicitly selected responses, with your identity and the exact review finding. It does not close findings or approve work. **Retry implementation and review** then supplies saved answers to both models. Unsaved answers must be saved before retrying. A changed review rejects a stale save and retains the editor draft.

Provider availability is not authorization. Direct execution requires accepted impacts, an approved current plan, an eligible task, and a satisfied global design barrier. Agent output is untrusted evidence until explicitly imported; only canonical CIS lifecycle commands and human decisions can approve, accept, transition, verify, or close work.
