---
title: "WORK-090 Implement backend domain and application behavior"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-090
task_type: core.backend.behavior
task_type_version: 1.0
parent_id: WORK-000
category: backend
complexity: medium
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-001","AGENT-002","AGENT-003","AGENT-004","AGENT-005","AGENT-006","AGENT-007","AGENT-008","AGENT-009","AGENT-010","AGENT-011","AGENT-012","AGENT-013","AGENT-014","AGENT-015","AGENT-016","AGENT-017","AGENT-018","AGENT-019","AGENT-020","AGENT-021","AGENT-022","AGENT-023","AGENT-024"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF","IMPACT-F2CD2C885A","IMPACT-8ABB646194","IMPACT-DBCDF4FAAD","IMPACT-1D7A625AEA"]
depends_on: ["WORK-040","WORK-080"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-090: Implement backend domain and application behavior

## Objective

Backend behavior satisfies positive, negative, state-transition, and concurrency requirements.

## Required changes

- `AGENT-001`: CIS shall discover agent providers through public contracts rather than hard-coded module branches.
- `AGENT-002`: `cis agent providers` and `cis agent provider diagnose <provider>` shall report truthful provider availability.
- `AGENT-003`: The portable envelope contract shall remain available and become the input to direct execution.
- `AGENT-004`: `cis agent run <change-id> <task-id>` shall start only an eligible, digest-current task.
- `AGENT-005`: Every run shall target an explicitly registered workspace repository and contained working directory.
- `AGENT-006`: The controller shall set an execution mode and permission ceiling before launch.
- `AGENT-007`: A run shall expose a durable state machine and append-only event stream.
- `AGENT-008`: Codex support shall use the supported rich-client boundary for stateful execution.
- `AGENT-009`: Codex non-interactive execution shall remain available as an explicit bounded fallback.
- `AGENT-010`: Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text.
- `AGENT-011`: Provider permission requests shall be mediated by CIS capability and policy.
- `AGENT-012`: `cis agent runs`, `show`, `cancel`, and `resume` shall manage observable runs without rewriting history.
- `AGENT-013`: Timeouts, interruption, and stale local locks shall have deterministic recovery.
- `AGENT-014`: Provider output shall be reconciled into the existing structured result and task-evidence model.
- `AGENT-015`: Canonical task state shall change only through an explicit CIS reconciliation step.
- `AGENT-016`: Derived run state shall live below `.cis/local/agents/runs/<run-id>/`.
- `AGENT-017`: Agent runs shall participate in CIS diagnostics, feedback, and cost accounting.
- `AGENT-018`: Credentials, provider configuration, prompts, output, and logs shall preserve repository privacy.
- `AGENT-019`: Process execution shall avoid shell interpolation and untrusted executable substitution.
- `AGENT-020`: Workspace policy shall define provider defaults and isolation without making a vendor mandatory.
- `AGENT-021`: Repository initialization shall seed provider-neutral instructions, skills, and a reference profile idempotently.
- `AGENT-022`: All agent commands shall provide stable human, JSON, and agent output suitable for a thin client.
- `AGENT-023`: Deterministic tests shall cover providers, lifecycle, security, recovery, and compatibility without requiring paid credentials.
- `AGENT-024`: Local smoke tests shall prove installed-client detection without requiring a live model request.

## Required outputs

- Completed backend artifacts
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

- `WORK-040` - Implement security, permissions, and exposure boundaries.
- `WORK-080` - Implement API and consumed contracts.

## Acceptance criteria

- [x] Backend behavior satisfies positive, negative, state-transition, and concurrency requirements.

## Targeted validation

- [x] Focused domain/application unit tests pass.
- [x] Affected backend integration tests pass.
- [x] Run focused domain, application, integration, concurrency, and regression tests.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Agent runtime | `tests/Cis.Modules.Agent.Tests` within `cis-0002-linux-20260828-r7` | Passed | 17 focused tests cover eligibility, execution, cancellation boundaries, recovery, resume, isolation, durable evidence, and malformed provider output. |
| Affected regression | `.cis/local/testing/results/dotnet-tests.trx` | Passed | Combined TRX contains 448/448 passed executions from every solution test project. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1025; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:3430d5b4a27cca6ce58e8b8725bf1ac2d4f1dd31af671926729b714a3f537396 |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
