---
title: "WORK-040 Implement security, permissions, and exposure boundaries"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-040
task_type: core.security.permissions
task_type_version: 1.0
parent_id: WORK-000
category: security
complexity: medium
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-001","AGENT-002","AGENT-003","AGENT-005","AGENT-006","AGENT-007","AGENT-008","AGENT-009","AGENT-010","AGENT-011","AGENT-014","AGENT-016","AGENT-018","AGENT-019","AGENT-021","AGENT-023","AGENT-024"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF","IMPACT-F2CD2C885A","IMPACT-8ABB646194","IMPACT-DBCDF4FAAD","IMPACT-1D7A625AEA"]
depends_on: ["WORK-030"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-040: Implement security, permissions, and exposure boundaries

## Objective

Positive and negative access paths enforce the approved security and visibility model.

## Required changes

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
- `AGENT-001`: CIS shall discover agent providers through public contracts rather than hard-coded module branches.
- `AGENT-002`: `cis agent providers` and `cis agent provider diagnose <provider>` shall report truthful provider availability.
- `AGENT-003`: The portable envelope contract shall remain available and become the input to direct execution.
- `AGENT-005`: Every run shall target an explicitly registered workspace repository and contained working directory.
- `AGENT-006`: The controller shall set an execution mode and permission ceiling before launch.
- `AGENT-007`: A run shall expose a durable state machine and append-only event stream.
- `AGENT-008`: Codex support shall use the supported rich-client boundary for stateful execution.
- `AGENT-009`: Codex non-interactive execution shall remain available as an explicit bounded fallback.
- `AGENT-010`: Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text.
- `AGENT-011`: Provider permission requests shall be mediated by CIS capability and policy.
- `AGENT-014`: Provider output shall be reconciled into the existing structured result and task-evidence model.
- `AGENT-016`: Derived run state shall live below `.cis/local/agents/runs/<run-id>/`.
- `AGENT-018`: Credentials, provider configuration, prompts, output, and logs shall preserve repository privacy.
- `AGENT-019`: Process execution shall avoid shell interpolation and untrusted executable substitution.
- `AGENT-021`: Repository initialization shall seed provider-neutral instructions, skills, and a reference profile idempotently.
- `AGENT-023`: Deterministic tests shall cover providers, lifecycle, security, recovery, and compatibility without requiring paid credentials.
- `AGENT-024`: Local smoke tests shall prove installed-client detection without requiring a live model request.

## Required outputs

- Completed security artifacts
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

## Acceptance criteria

- [x] Positive and negative access paths enforce the approved security and visibility model.

## Targeted validation

- [x] Positive and prohibited access paths are tested.
- [x] Sensitive data and secrets are not exposed.
- [x] Run policy, authorization, data-exposure, secret, abuse-case, and security regression checks.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Permission and containment tests | `.cis/local/testing/runs/cis-0002-linux-20260828-r7/results.json` | Passed | Exact agent cases cover ceiling enforcement, worktree isolation, path containment, environment allowlisting, redaction, and fail-closed evidence. |
| Security scanners | `.cis/local/workflows/cis-0002-linux-20260828-r7/{cis-sast,cis-secrets,cis-filesystem}.log` | Passed | Semgrep SAST, bounded secret scanning, and Trivy filesystem scanning completed without findings. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1021; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:85add7c4c64655e3da5d7d8d36e79cec150a5f1dc4031eeb87821603e7c2de75 |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
