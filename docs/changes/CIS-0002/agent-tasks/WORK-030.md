---
title: "WORK-030 Align specifications, contracts, and references"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-030
task_type: core.documentation.contracts
task_type_version: 1.0
parent_id: WORK-000
category: documentation
complexity: low
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-001","AGENT-002","AGENT-003","AGENT-004","AGENT-005","AGENT-006","AGENT-007","AGENT-008","AGENT-009","AGENT-010","AGENT-011","AGENT-012","AGENT-013","AGENT-014","AGENT-015","AGENT-016","AGENT-017","AGENT-018","AGENT-019","AGENT-020","AGENT-021","AGENT-022","AGENT-023","AGENT-024"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF"]
depends_on: []
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-030: Align specifications, contracts, and references

## Objective

All affected canonical documentation agrees with approved behavior and exclusions.

## Required changes

- cis agent providers
- cis agent provider diagnose <provider>
- cis agent prepare <change-id> <task-id> [--provider <provider>]
- cis agent run <change-id> <task-id> --provider <provider> --mode <mode> --permission <ceiling> [--target <repository-id>] [--transport <transport>]
- cis agent runs [--change <change-id>] [--task <task-id>] [--status <status>]
- cis agent show <run-id>
- cis agent cancel <run-id> --actor <actor> --reason <reason>
- cis agent resume <run-id> [--message <message>] --actor <actor> --reason <reason>
- cis agent import-result <envelope> --result <result>
- cis agent status
- `run` is foreground and streams normalized events by default. A future editor client
- owns that CLI process and cancellation. Detached local daemons, cloud scheduling, and
- remote agent farms are excluded from this slice. If the client disconnects, CIS records
- the interruption and a later explicit resume may create another attempt.
- The Codex boundary is based on the official App Server and non-interactive execution
- contracts:
- <https://learn.chatgpt.com/docs/app-server>
- <https://developers.openai.com/codex/non-interactive-mode>
- The Claude boundary shall be verified against the official Claude Code CLI and Agent
- SDK documentation during adapter implementation. Provider-specific behavior remains
- behind capabilities so later vendor changes do not alter canonical CIS authority.
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

## Required outputs

- Updated canonical specifications, contracts, decisions, and references

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

## Dependencies and approval gates

- No task dependencies.

## Acceptance criteria

- [x] All affected canonical documentation agrees with approved behavior and exclusions.

## Targeted validation

- [x] Strict documentation and front-matter validation pass.
- [x] Affected contract/reference drift checks pass.
- [x] Run strict documentation and applicable contract/reference drift validation.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Canonical documentation | `cis docs validate --repo . --strict` in `cis-0002-linux-20260828-r7` | Passed | 411 catalogued documents; provider profile, command manuals, skills, instructions, and module/reference ownership agree. |
| Reference governance | `cis references validate --repo . --strict` | Passed | Canonical `.cis/repository.yml` is observable; agent and provider components are classified. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1019; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:c21c5d6258d8a6f2b47e7a76c2bc77851e55ee2c48861f0eb687cc08bfed37d1 |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.
## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|
## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
