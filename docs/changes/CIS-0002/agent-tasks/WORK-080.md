---
title: "WORK-080 Implement API and consumed contracts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-080
task_type: core.api.contract
task_type_version: 1.0
parent_id: WORK-000
category: contract
complexity: medium
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-001","AGENT-003","AGENT-008","AGENT-009","AGENT-010","AGENT-011","AGENT-014","AGENT-017","AGENT-022","AGENT-024"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF","IMPACT-F2CD2C885A","IMPACT-8ABB646194","IMPACT-DBCDF4FAAD","IMPACT-1D7A625AEA"]
depends_on: ["WORK-030","WORK-040"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-080: Implement API and consumed contracts

## Objective

The API inventory, OpenAPI baseline, permissions, errors, consumers, and implementation agree for compatible positive and negative behavior.

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
- Direct execution is disabled in untrusted or unresolved repositories.
- A provider receives only the selected context pack and task contract; CIS does not
- dump the entire graph, `.cis/local/`, credentials, dependency folders, or unrelated
- repository content into the prompt.
- `workspace-write` permits edits only inside the selected target/worktree. It does not
- imply network, administrative, credential, publish, push, merge, deployment, or
- destructive authority.
- Provider process descendants are owned and cancelled as one bounded process tree.
- Structured provider events are data. They cannot invoke CIS commands or grant
- permission without a validated controller action.
- The Codex boundary is based on the official App Server and non-interactive execution
- contracts:
- <https://learn.chatgpt.com/docs/app-server>
- <https://developers.openai.com/codex/non-interactive-mode>
- The Claude boundary shall be verified against the official Claude Code CLI and Agent
- SDK documentation during adapter implementation. Provider-specific behavior remains
- behind capabilities so later vendor changes do not alter canonical CIS authority.
- `AGENT-001`: CIS shall discover agent providers through public contracts rather than hard-coded module branches.
- `AGENT-003`: The portable envelope contract shall remain available and become the input to direct execution.
- `AGENT-008`: Codex support shall use the supported rich-client boundary for stateful execution.
- `AGENT-009`: Codex non-interactive execution shall remain available as an explicit bounded fallback.
- `AGENT-010`: Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text.
- `AGENT-011`: Provider permission requests shall be mediated by CIS capability and policy.
- `AGENT-014`: Provider output shall be reconciled into the existing structured result and task-evidence model.
- `AGENT-017`: Agent runs shall participate in CIS diagnostics, feedback, and cost accounting.
- `AGENT-022`: All agent commands shall provide stable human, JSON, and agent output suitable for a thin client.

## Required outputs

- Completed contract artifacts
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

- `WORK-030` - Align specifications, contracts, and references.
- `WORK-040` - Implement security, permissions, and exposure boundaries.

## Acceptance criteria

- [x] The API inventory, OpenAPI baseline, permissions, errors, consumers, and implementation agree for compatible positive and negative behavior.

## Targeted validation

- [x] Positive, validation-failure, unauthorized, forbidden, and compatibility cases pass.
- [x] Run handler, authorization, abuse, validator, compatibility, integration, schema, OpenAPI-diff, and contract-drift checks.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Public provider contracts | `tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs` and `cis-0002-linux-20260828-r7` | Passed | Codex App Server/JSONL and Claude stream-JSON adapters normalize versioned commands, events, sessions, usage, failures, and structured completion. |
| Compatibility | `dotnet test ChangeImpactStudio.slnx -c Release` through the aggregate runner | Passed | 448/448 tests include schema-v1 envelope compatibility and all human/JSON/agent command surfaces. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1023; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:b276b8bb6d9daf57684175c7e047beb8a7531f2c6d0ace5966bd252a09972cf1 |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
