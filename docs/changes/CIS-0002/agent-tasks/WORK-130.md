---
title: "WORK-130 Implement observability and operational readiness"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-130
task_type: core.operations.observability
task_type_version: 1.0
parent_id: WORK-000
category: observability
complexity: medium
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-016"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF","IMPACT-F2CD2C885A","IMPACT-8ABB646194","IMPACT-DBCDF4FAAD","IMPACT-1D7A625AEA"]
depends_on: ["WORK-090"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-130: Implement observability and operational readiness

## Objective

Operators can detect, diagnose, and respond to expected failures without exposing sensitive data.

## Required changes

- `AGENT-016`: Derived run state shall live below `.cis/local/agents/runs/<run-id>/`.

## Required outputs

- Completed observability artifacts
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

- [x] Operators can detect, diagnose, and respond to expected failures without exposing sensitive data.

## Targeted validation

- [x] Telemetry, redaction, alert, dashboard, runbook, and failure-detection evidence is recorded.
- [x] Validate telemetry emission, redaction, alert behavior, dashboards, runbooks, and failure drills.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Durable diagnostics | `cis agent runs`, `cis agent show`, `cis agent status`, and `.cis/local/agents/runs/<run-id>/` | Passed | Manifests, monotonic events, attempts, permission records, process identity, failure kind, usage, redaction outcome, and artifact hashes are queryable. |
| Failure drill | `.cis/local/workflows/cis-0002-linux-20260828-r6/state.json` | Passed | Missing CLI prerequisite was preserved as `missing-prerequisite`; corrected R7 remained a separate run. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1027; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:0e950e46d990683d1264d71f4fe4ce33c2bce66526d048ffe161ceb83f5e95d8 |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
