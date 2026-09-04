---
title: "WORK-180 Run final delivery sweep and handoff"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-180
task_type: core.delivery.final-sweep
task_type_version: 1.0
parent_id: WORK-000
category: delivery
complexity: low
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
requirement_ids: ["AGENT-001","AGENT-002","AGENT-003","AGENT-004","AGENT-005","AGENT-006","AGENT-007","AGENT-008","AGENT-009","AGENT-010","AGENT-011","AGENT-012","AGENT-013","AGENT-014","AGENT-015","AGENT-016","AGENT-017","AGENT-018","AGENT-019","AGENT-020","AGENT-021","AGENT-022","AGENT-023","AGENT-024"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-A57634A7AF","IMPACT-F2CD2C885A","IMPACT-8ABB646194","IMPACT-DBCDF4FAAD","IMPACT-1D7A625AEA"]
depends_on: ["WORK-030","WORK-040","WORK-080","WORK-090","WORK-130","WORK-140","WORK-160","WORK-170"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-180: Run final delivery sweep and handoff

## Objective

Every child has a valid disposition and the final outcome is reproducible without converting blockers into passes.

## Required changes

- Contract tests validate provider discovery, stable capabilities, schema evolution,
- event normalization, result mapping, and backward-compatible portable envelopes.
- Lifecycle tests use deterministic fake processes/protocol servers and a controllable
- clock to cover every state transition, timeout, interruption, resume, lock, and
- recovery path.
- Security tests cover path containment, argument construction, environment filtering,
- redaction, output limits, credential non-persistence, request-above-ceiling denial,
- untrusted repository rejection, and process-tree cancellation.
- Provider adapter fixtures replay official structured Codex and Claude message shapes;
- malformed, unknown, duplicated, stale, and oversized messages remain safe.
- Initialization, Doctor, manuals, skills, instructions, package, Linux, and installed
- executable detection are included in the normal CIS verification chain.
- Live Codex or Claude model calls are opt-in smoke tests and are not required for
- deterministic completion.
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

- Planned-versus-actual report
- Reproducible handoff evidence

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
- `WORK-080` - Implement API and consumed contracts.
- `WORK-090` - Implement backend domain and application behavior.
- `WORK-130` - Implement observability and operational readiness.
- `WORK-140` - Implement lifecycle and carry-forward behavior.
- `WORK-160` - Complete targeted and regression verification.
- `WORK-170` - Perform independent assurance.

## Acceptance criteria

- [x] Every child has a valid disposition and the final outcome is reproducible without converting blockers into passes.
- [x] `AGENT-001`: Loaded provider assemblies register a stable identifier, display name, transport, executable requirements, supported run modes, permission capabilities, resume behavior, and version diagnostics; duplicate identifiers fail deterministically.
- [x] `AGENT-002`: Human, JSON, and agent output distinguish executable missing, incompatible version, authentication unavailable, unsupported capability, unhealthy transport, and ready; no token, key, cookie, or credential value is read or emitted.
- [x] `AGENT-003`: Existing envelope schema version 1 remains readable; a new version records task digest, accepted scope digests, context manifest, repository revision or working-tree digest, target repository, run mode, provider, permission ceiling, and expiry without silently weakening old validation.
- [x] `AGENT-004`: CIS rejects unknown, blocked, complete, stale, unapproved-plan, unapproved-design-gate, unresolved-impact, ambiguous-target, or scope-conflict tasks before launching a provider and reports the exact gate.
- [x] `AGENT-005`: The target is selected from the task and workspace registry, repository-relative paths are containment checked, multi-repository tasks require an explicit target per execution, and neither current-directory coincidence nor provider output can change authority.
- [x] `AGENT-006`: Modes are `plan`, `implement`, and `review`; permission ceilings are `read-only` and `workspace-write`; network, secret access, unrestricted host access, and destructive operations are never inferred from task prose.
- [x] `AGENT-007`: States include `Prepared`, `Starting`, `Running`, `AwaitingPermission`, `Cancelling`, `Cancelled`, `Succeeded`, `Failed`, `TimedOut`, `InvalidEvidence`, and `Interrupted`; events have monotonic sequence, UTC timestamp, attempt, kind, redacted payload, and provider correlation identity.
- [x] `AGENT-008`: The Codex adapter can start and communicate with Codex App Server over structured messages, records server and protocol versions, streams thread/turn/item events, maps approval requests into CIS permission events, and retains the Codex thread identity for supported continuation.
- [x] `AGENT-009`: The adapter can invoke `codex exec --json` using an argument array, parse JSONL events, record that interactive approvals are unavailable, preserve the session identity needed for supported resume, and never silently substitutes fallback for a requested App Server capability.
- [x] `AGENT-010`: The Claude adapter invokes structured print-mode or SDK execution, consumes JSON or streaming JSON, records CLI/SDK version and session identity, applies declared tool and permission settings, and reports unsupported interactive permission or resume capabilities rather than inventing parity.
- [x] `AGENT-011`: A supported request records provider, run, attempt, requested capability, bounded target, rationale, response actor, response, and UTC timestamp; a request above the declared ceiling is denied; non-interactive transports fail as `permission-required` without retrying more permissively.
- [x] `AGENT-012`: Listing and detail are derived from durable state; cancellation terminates the owned process tree and remains distinguishable from failure; resume creates a new attempt linked to the same CIS run and uses provider continuation only when its recorded capability supports it.
- [x] `AGENT-013`: Each run has bounded startup, idle, and total timeouts; one active run per task and target is the default; orphaned process identities are diagnosed before lock recovery; a retry never erases the first attempt or its failure classification.
- [x] `AGENT-014`: CIS validates envelope identity and freshness, changed-file containment, declared target, summary, validations, artifacts, and terminal status; unreadable, missing, stale, or partial expected results become `InvalidEvidence`, never `Succeeded`.
- [x] `AGENT-015`: A provider cannot write approval or lifecycle evidence directly; imported results append provenance to the task and may support a later governed transition, but do not automatically mark the task or change complete.
- [x] `AGENT-016`: Each run retains a manifest, append-only events, attempts, permission records, bounded stdout/stderr or protocol logs, result, artifact inventory, and digests; canonical documents store only reviewed summaries and durable evidence references.
- [x] `AGENT-017`: Tool calls, durations, exit/failure classifications, provider-reported token usage and cost when available, estimated context/token savings, redaction outcomes, and artifact hashes use existing CIS evidence contracts and remain attributable to run and attempt.
- [x] `AGENT-018`: CIS relies on provider-native authenticated sessions or named environment references, never persists secret values, redacts configured patterns before persistence or display, bounds log and artifact size, and excludes secret/dependency paths from context packaging.
- [x] `AGENT-019`: Executables are resolved from explicit profile or trusted PATH discovery, arguments use process argument lists, working directories are contained, inherited environment is allowlisted where required, process identity is recorded, and output cannot be interpreted as CIS commands.
- [x] `AGENT-020`: No provider is selected silently unless a canonical provider profile declares the default; Git repositories default to an isolated worktree for implementation runs; direct dirty-working-tree execution requires an explicit profile policy and records a pre-run snapshot.
- [x] `AGENT-021`: Reinitialization preserves reviewed policy; agents are instructed to run `repo init`, use Repository Doctor after failure, obey approved scope and design barriers, avoid lifecycle authority, and reconcile results; Doctor reports missing profile, unavailable declared default, incompatible executable, stale run, orphaned process, and unsafe policy.
- [x] `AGENT-022`: JSON uses versioned contracts and stable status/failure codes; human output is concise; agent output is single-record oriented; provider-native events remain encapsulated behind normalized CIS event kinds with an inspectable bounded raw reference.
- [x] `AGENT-023`: Fixtures cover fake Codex App Server, Codex JSONL, Claude streaming JSON, version/auth failures, permission approve/deny/unsupported, cancellation, timeout, crash, malformed/oversized output, stale envelopes, path escapes, duplicate providers, resume, orphan recovery, redaction, schema v1 compatibility, and all output formats.
- [x] `AGENT-024`: Smoke checks may inspect executable version, structured help/protocol initialization, and authentication status only; credential-dependent live calls are opt-in and reported `Unavailable` when not configured, never passed by substitution.

## Targeted validation

- [x] Final repository completion and planned-versus-actual checks are recorded.
- [x] Run the repository completion gate, strict docs validation, final affected checks, and evidence audit.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Release assurance | `cis-0002-release-20260828-r9` | Passed | One reconciled manifest contains 448/448 passed tests, 24/24 exact traces, 78.6% line coverage, and passed mutation assurance. |
| Security delivery | `cis-0002-linux-20260828-r7` and final `cis-0002-security-20260828-r10` | Passed | Full R7 delivery passed and Semgrep, secret scanning, and Trivy were repeated successfully against the final canonical snapshot on the `/data` Linux runner. |
| Repository governance | strict docs, skills, references, idempotent init, graph, index, and Doctor | Passed | 413 documents, 43 skills, zero reference errors, 63 components with no init delta, 10,411 graph nodes/48,488 edges with zero warnings, and 916 fresh routing cards. |
| Package smoke | NuGet tool 0.3.0 and VSIX 0.3.0 under `artifacts/cis-0002-agent/` | Passed | Packaged CLI registers the Agent, Codex, and Claude modules and exposes the complete lifecycle command surface; VSIX construction passes Windows PowerShell 5.1. |
| Residual risk | CIS-0002 verification and assurance ledgers | Passed | No product deferrals, test exceptions, or unresolved security findings; unavailable provider credentials were not required or treated as passed. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1051; failed=83; possibleTokenSavings=453430; ledgerDigest=sha256:2c7b82d16fc74f41f92ae0ac671cd5dfd9e7d8851aae36f91afeabb51ea0036c |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
