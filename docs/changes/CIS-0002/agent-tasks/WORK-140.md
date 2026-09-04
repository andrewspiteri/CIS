---
title: "WORK-140 Implement lifecycle and carry-forward behavior"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-140
task_type: core.lifecycle.carry-forward
task_type_version: 1.0
parent_id: WORK-000
category: lifecycle
complexity: medium
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-015"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF","IMPACT-F2CD2C885A","IMPACT-8ABB646194","IMPACT-DBCDF4FAAD","IMPACT-1D7A625AEA"]
depends_on: ["WORK-090"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-140: Implement lifecycle and carry-forward behavior

## Objective

Transitions avoid duplication and preserve approved access, history, and canonical ownership.

## Required changes

- `AGENT-015`: Canonical task state shall change only through an explicit CIS reconciliation step.

## Required outputs

- Completed lifecycle artifacts
- Reproducible validation evidence

## Constraints and exclusions

- Implementing the CIS-0001 Visual Studio Code UI, chat transcript, or editor commands.
- Replacing provider-native model selection, authentication, or account management.
- Storing API keys, OAuth tokens, provider cookies, or secret values in CIS Markdown or
- derived state.
- Autonomous plan, impact, design, exception, scope-expansion, or completion approval.
- Detached daemons, remote worker queues, cloud agent scheduling, or multi-host session
- migration.
- Treating vendor token/cost reporting as billing authority when a provider does not
- supply it.
- Parsing decorated terminal output or directly integrating with another editor
- extension's private APIs.

## Context and evidence

- Canonical feature specification: `docs/specs/features/agent-execution-coordination-feature.md` (`sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3`).
- Accepted impact `IMPACT-196BBD00E2`: `change-impact-studio::document::change-impact-studio:feature:agent-execution-coordination` - Provider-Neutral Agent Execution Coordination.
- Accepted impact `IMPACT-A57634A7AF`: `change-impact-studio::document::change-impact-studio:spec:module-catalog` - Change Impact Studio Module Catalog.
- Accepted impact `IMPACT-F2CD2C885A`: `change-impact-studio::component::cis-modules-agent` - cis-modules-agent.
- Accepted impact `IMPACT-8ABB646194`: `change-impact-studio::source-file::src/Cis.Modules.Agent/AgentModule.cs` - AgentModule.cs.
- Accepted impact `IMPACT-DBCDF4FAAD`: `change-impact-studio::source-file::src/Cis.Modules.Agent/AgentService.cs` - AgentService.cs.
- Accepted impact `IMPACT-1D7A625AEA`: `change-impact-studio::repository::change-impact-studio` - change-impact-studio.

## Dependencies and approval gates

- `WORK-090` - Implement backend domain and application behavior.

## Acceptance criteria

- [x] Transitions avoid duplication and preserve approved access, history, and canonical ownership.
- [x] `AGENT-015`: A provider cannot write approval or lifecycle evidence directly; imported results append provenance to the task and may support a later governed transition, but do not automatically mark the task or change complete.

## Targeted validation

- [x] Transition, carry-forward, provenance, audit, visibility, and non-duplication tests pass.
- [x] Run transition, carry-forward, non-duplication, audit, permission, and retention tests.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Attempt lifecycle | `TC-AGENT-012-001` and `TC-AGENT-013-001` in R7 | Passed | Resume appends an immutable attempt; orphan recovery validates process identity and releases only the matching lock. |
| Authority boundary | `TC-AGENT-014-001` and `TC-AGENT-015-001` in R7 | Passed | Provider results are validated and retained but cannot approve or complete canonical tasks. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1029; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:e0af6215403e864905469da094fd97c41b627f1f42a7f41600bb78313a50803f |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
