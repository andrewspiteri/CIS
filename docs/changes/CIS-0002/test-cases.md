---
title: "CIS-0002 manual test cases"
type: manual-test-cases
status: Draft
change_id: CIS-0002
feature_spec_path: "docs/specs/features/agent-execution-coordination-feature.md"
feature_spec_sha256: "sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3"
test_case_count: 24
automated_test_case_count: 24
automation_pending_count: 0
csv_path: test-cases.csv
csv_sha256: "sha256:cd1b68cb8031b1b566fc44699361df93c30ced1ba197ff066705e3c43ce3fd83"
generation: deterministic
authority: derived
---

# Manual test cases

Generated from `docs/specs/features/agent-execution-coordination-feature.md`. Regenerate through feature planning after the source specification changes; do not record execution results in this derived catalogue.

The companion `test-cases.csv` uses one row per case and portable field names that can be mapped during import into TestRail or another test-management system.

## Coverage summary

| Test case | Title | Section | Priority | Type | Requirement | Frontend type | Automation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TC-AGENT-001-001 | CIS shall discover agent providers through public contracts rather than hard-coded module branches. | Contract | Low | Security | AGENT-001 | not-applicable | Automated |
| TC-AGENT-002-001 | `cis agent providers` and `cis agent provider diagnose <provider>` shall report truthful provider availability. | Backend | Low | Functional | AGENT-002 | not-applicable | Automated |
| TC-AGENT-003-001 | The portable envelope contract shall remain available and become the input to direct execution. | Contract | Low | Security | AGENT-003 | not-applicable | Automated |
| TC-AGENT-004-001 | `cis agent run <change-id> <task-id>` shall start only an eligible, digest-current task. | Backend | Low | Functional | AGENT-004 | not-applicable | Automated |
| TC-AGENT-005-001 | Every run shall target an explicitly registered workspace repository and contained working directory. | Backend | Low | Functional | AGENT-005 | not-applicable | Automated |
| TC-AGENT-006-001 | The controller shall set an execution mode and permission ceiling before launch. | Security | Medium | Security | AGENT-006 | not-applicable | Automated |
| TC-AGENT-007-001 | A run shall expose a durable state machine and append-only event stream. | Backend | Low | Security | AGENT-007 | not-applicable | Automated |
| TC-AGENT-008-001 | Codex support shall use the supported rich-client boundary for stateful execution. | Contract | Low | Security | AGENT-008 | not-applicable | Automated |
| TC-AGENT-009-001 | Codex non-interactive execution shall remain available as an explicit bounded fallback. | Contract | Low | API and contract | AGENT-009 | not-applicable | Automated |
| TC-AGENT-010-001 | Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text. | Contract | Low | Security | AGENT-010 | not-applicable | Automated |
| TC-AGENT-011-001 | Provider permission requests shall be mediated by CIS capability and policy. | Security | Medium | Security | AGENT-011 | not-applicable | Automated |
| TC-AGENT-012-001 | `cis agent runs`, `show`, `cancel`, and `resume` shall manage observable runs without rewriting history. | Backend | Low | Functional | AGENT-012 | not-applicable | Automated |
| TC-AGENT-013-001 | Timeouts, interruption, and stale local locks shall have deterministic recovery. | Backend | Low | Functional | AGENT-013 | not-applicable | Automated |
| TC-AGENT-014-001 | Provider output shall be reconciled into the existing structured result and task-evidence model. | Contract | Low | API and contract | AGENT-014 | not-applicable | Automated |
| TC-AGENT-015-001 | Canonical task state shall change only through an explicit CIS reconciliation step. | Backend | Low | Functional | AGENT-015 | not-applicable | Automated |
| TC-AGENT-016-001 | Derived run state shall live below `.cis/local/agents/runs/<run-id>/`. | Backend | Low | Security | AGENT-016 | not-applicable | Automated |
| TC-AGENT-017-001 | Agent runs shall participate in CIS diagnostics, feedback, and cost accounting. | Backend | Low | API and contract | AGENT-017 | not-applicable | Automated |
| TC-AGENT-018-001 | Credentials, provider configuration, prompts, output, and logs shall preserve repository privacy. | Security | Low | Security | AGENT-018 | not-applicable | Automated |
| TC-AGENT-019-001 | Process execution shall avoid shell interpolation and untrusted executable substitution. | Security | Low | Security | AGENT-019 | not-applicable | Automated |
| TC-AGENT-020-001 | Workspace policy shall define provider defaults and isolation without making a vendor mandatory. | Governance | Low | Functional | AGENT-020 | not-applicable | Automated |
| TC-AGENT-021-001 | Repository initialization shall seed provider-neutral instructions, skills, and a reference profile idempotently. | Delivery | Low | Functional | AGENT-021 | not-applicable | Automated |
| TC-AGENT-022-001 | All agent commands shall provide stable human, JSON, and agent output suitable for a thin client. | Contract | Low | API and contract | AGENT-022 | not-applicable | Automated |
| TC-AGENT-023-001 | Deterministic tests shall cover providers, lifecycle, security, recovery, and compatibility without requiring paid cr... | Testing | Medium | Security | AGENT-023 | not-applicable | Automated |
| TC-AGENT-024-001 | Local smoke tests shall prove installed-client detection without requiring a live model request. | Delivery | Low | API and contract | AGENT-024 | not-applicable | Automated |

## Test case definitions

### TC-AGENT-001-001: CIS shall discover agent providers through public contracts rather than hard-coded module branches.

- Requirement: `AGENT-001`
- Section: Contract
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:18`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:19`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-001.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-001.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: CIS shall discover agent providers through public contracts rather than hard-coded module branches.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Loaded provider assemblies register a stable identifier, display name, transport, executable requirements, supported run modes, permission capabilities, resume behavior, and version diagnostics; duplicate identifiers fail deterministically.

### TC-AGENT-002-001: `cis agent providers` and `cis agent provider diagnose <provider>` shall report truthful provider availability.

- Requirement: `AGENT-002`
- Section: Backend
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:18`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:19`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-002.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-002.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: `cis agent providers` and `cis agent provider diagnose <provider>` shall report truthful provider availability.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Human, JSON, and agent output distinguish executable missing, incompatible version, authentication unavailable, unsupported capability, unhealthy transport, and ready; no token, key, cookie, or credential value is read or emitted.

### TC-AGENT-003-001: The portable envelope contract shall remain available and become the input to direct execution.

- Requirement: `AGENT-003`
- Section: Contract
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:256`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:75`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:77`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-003.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-003.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The portable envelope contract shall remain available and become the input to direct execution.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Existing envelope schema version 1 remains readable; a new version records task digest, accepted scope digests, context manifest, repository revision or working-tree digest, target repository, run mode, provider, permission ceiling, and expiry without silently weakening old validation.

### TC-AGENT-004-001: `cis agent run <change-id> <task-id>` shall start only an eligible, digest-current task.

- Requirement: `AGENT-004`
- Section: Backend
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:115`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:116`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-004.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-004.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: `cis agent run <change-id> <task-id>` shall start only an eligible, digest-current task.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

CIS rejects unknown, blocked, complete, stale, unapproved-plan, unapproved-design-gate, unresolved-impact, ambiguous-target, or scope-conflict tasks before launching a provider and reports the exact gate.

### TC-AGENT-005-001: Every run shall target an explicitly registered workspace repository and contained working directory.

- Requirement: `AGENT-005`
- Section: Backend
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:150`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:151`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:256`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-005.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-005.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Every run shall target an explicitly registered workspace repository and contained working directory.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The target is selected from the task and workspace registry, repository-relative paths are containment checked, multi-repository tasks require an explicit target per execution, and neither current-directory coincidence nor provider output can change authority.

### TC-AGENT-006-001: The controller shall set an execution mode and permission ceiling before launch.

- Requirement: `AGENT-006`
- Section: Security
- Priority: Medium
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:150`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:151`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:279`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:281`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-006.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-006.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The controller shall set an execution mode and permission ceiling before launch.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Modes are `plan`, `implement`, and `review`; permission ceilings are `read-only` and `workspace-write`; network, secret access, unrestricted host access, and destructive operations are never inferred from task prose.

### TC-AGENT-007-001: A run shall expose a durable state machine and append-only event stream.

- Requirement: `AGENT-007`
- Section: Backend
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:75`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:77`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-007.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-007.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: A run shall expose a durable state machine and append-only event stream.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

States include `Prepared`, `Starting`, `Running`, `AwaitingPermission`, `Cancelling`, `Cancelled`, `Succeeded`, `Failed`, `TimedOut`, `InvalidEvidence`, and `Interrupted`; events have monotonic sequence, UTC timestamp, attempt, kind, redacted payload, and provider correlation identity.

### TC-AGENT-008-001: Codex support shall use the supported rich-client boundary for stateful execution.

- Requirement: `AGENT-008`
- Section: Contract
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:279`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:300`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:301`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-008.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-008.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Codex support shall use the supported rich-client boundary for stateful execution.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The Codex adapter can start and communicate with Codex App Server over structured messages, records server and protocol versions, streams thread/turn/item events, maps approval requests into CIS permission events, and retains the Codex thread identity for supported continuation.

### TC-AGENT-009-001: Codex non-interactive execution shall remain available as an explicit bounded fallback.

- Requirement: `AGENT-009`
- Section: Contract
- Priority: Low
- Type: API and contract
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:279`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:300`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:301`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-009.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-009.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Codex non-interactive execution shall remain available as an explicit bounded fallback.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The adapter can invoke `codex exec --json` using an argument array, parse JSONL events, record that interactive approvals are unavailable, preserve the session identity needed for supported resume, and never silently substitutes fallback for a requested App Server capability.

### TC-AGENT-010-001: Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text.

- Requirement: `AGENT-010`
- Section: Contract
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:300`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:301`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-010.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-010.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The Claude adapter invokes structured print-mode or SDK execution, consumes JSON or streaming JSON, records CLI/SDK version and session identity, applies declared tool and permission settings, and reports unsupported interactive permission or resume capabilities rather than inventing parity.

### TC-AGENT-011-001: Provider permission requests shall be mediated by CIS capability and policy.

- Requirement: `AGENT-011`
- Section: Security
- Priority: Medium
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:279`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:281`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-011.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-011.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Provider permission requests shall be mediated by CIS capability and policy.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

A supported request records provider, run, attempt, requested capability, bounded target, rationale, response actor, response, and UTC timestamp; a request above the declared ceiling is denied; non-interactive transports fail as `permission-required` without retrying more permissively.

### TC-AGENT-012-001: `cis agent runs`, `show`, `cancel`, and `resume` shall manage observable runs without rewriting history.

- Requirement: `AGENT-012`
- Section: Backend
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:171`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:172`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:325`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:326`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-012.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-012.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: `cis agent runs`, `show`, `cancel`, and `resume` shall manage observable runs without rewriting history.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Listing and detail are derived from durable state; cancellation terminates the owned process tree and remains distinguishable from failure; resume creates a new attempt linked to the same CIS run and uses provider continuation only when its recorded capability supports it.

### TC-AGENT-013-001: Timeouts, interruption, and stale local locks shall have deterministic recovery.

- Requirement: `AGENT-013`
- Section: Backend
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:171`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:193`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:325`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:326`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-013.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-013.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Timeouts, interruption, and stale local locks shall have deterministic recovery.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Each run has bounded startup, idle, and total timeouts; one active run per task and target is the default; orphaned process identities are diagnosed before lock recovery; a retry never erases the first attempt or its failure classification.

### TC-AGENT-014-001: Provider output shall be reconciled into the existing structured result and task-evidence model.

- Requirement: `AGENT-014`
- Section: Contract
- Priority: Low
- Type: API and contract
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:256`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:75`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:77`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-014.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-014.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Provider output shall be reconciled into the existing structured result and task-evidence model.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

CIS validates envelope identity and freshness, changed-file containment, declared target, summary, validations, artifacts, and terminal status; unreadable, missing, stale, or partial expected results become `InvalidEvidence`, never `Succeeded`.

### TC-AGENT-015-001: Canonical task state shall change only through an explicit CIS reconciliation step.

- Requirement: `AGENT-015`
- Section: Backend
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:75`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:77`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-015.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-015.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Canonical task state shall change only through an explicit CIS reconciliation step.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

A provider cannot write approval or lifecycle evidence directly; imported results append provenance to the task and may support a later governed transition, but do not automatically mark the task or change complete.

### TC-AGENT-016-001: Derived run state shall live below `.cis/local/agents/runs/<run-id>/`.

- Requirement: `AGENT-016`
- Section: Backend
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:76`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:77`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-016.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-016.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Derived run state shall live below `.cis/local/agents/runs/<run-id>/`.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Each run retains a manifest, append-only events, attempts, permission records, bounded stdout/stderr or protocol logs, result, artifact inventory, and digests; canonical documents store only reviewed summaries and durable evidence references.

### TC-AGENT-017-001: Agent runs shall participate in CIS diagnostics, feedback, and cost accounting.

- Requirement: `AGENT-017`
- Section: Backend
- Priority: Low
- Type: API and contract
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:76`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:77`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-017.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-017.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Agent runs shall participate in CIS diagnostics, feedback, and cost accounting.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Tool calls, durations, exit/failure classifications, provider-reported token usage and cost when available, estimated context/token savings, redaction outcomes, and artifact hashes use existing CIS evidence contracts and remain attributable to run and attempt.

### TC-AGENT-018-001: Credentials, provider configuration, prompts, output, and logs shall preserve repository privacy.

- Requirement: `AGENT-018`
- Section: Security
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:280`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:281`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:357`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-018.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-018.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Credentials, provider configuration, prompts, output, and logs shall preserve repository privacy.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

CIS relies on provider-native authenticated sessions or named environment references, never persists secret values, redacts configured patterns before persistence or display, bounds log and artifact size, and excludes secret/dependency paths from context packaging.

### TC-AGENT-019-001: Process execution shall avoid shell interpolation and untrusted executable substitution.

- Requirement: `AGENT-019`
- Section: Security
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:150`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:151`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:280`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:281`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:325`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:326`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-019.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-019.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Process execution shall avoid shell interpolation and untrusted executable substitution.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Executables are resolved from explicit profile or trusted PATH discovery, arguments use process argument lists, working directories are contained, inherited environment is allowlisted where required, process identity is recorded, and output cannot be interpreted as CIS commands.

### TC-AGENT-020-001: Workspace policy shall define provider defaults and isolation without making a vendor mandatory.

- Requirement: `AGENT-020`
- Section: Governance
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:150`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:151`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-020.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-020.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Workspace policy shall define provider defaults and isolation without making a vendor mandatory.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

No provider is selected silently unless a canonical provider profile declares the default; Git repositories default to an isolated worktree for implementation runs; direct dirty-working-tree execution requires an explicit profile policy and records a pre-run snapshot.

### TC-AGENT-021-001: Repository initialization shall seed provider-neutral instructions, skills, and a reference profile idempotently.

- Requirement: `AGENT-021`
- Section: Delivery
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:46`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:47`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-021.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-021.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Repository initialization shall seed provider-neutral instructions, skills, and a reference profile idempotently.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Reinitialization preserves reviewed policy; agents are instructed to run `repo init`, use Repository Doctor after failure, obey approved scope and design barriers, avoid lifecycle authority, and reconcile results; Doctor reports missing profile, unavailable declared default, incompatible executable, stale run, orphaned process, and unsafe policy.

### TC-AGENT-022-001: All agent commands shall provide stable human, JSON, and agent output suitable for a thin client.

- Requirement: `AGENT-022`
- Section: Contract
- Priority: Low
- Type: API and contract
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:18`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:19`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:300`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:301`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-022.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-022.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: All agent commands shall provide stable human, JSON, and agent output suitable for a thin client.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

JSON uses versioned contracts and stable status/failure codes; human output is concise; agent output is single-record oriented; provider-native events remain encapsulated behind normalized CIS event kinds with an inspectable bounded raw reference.

### TC-AGENT-023-001: Deterministic tests shall cover providers, lifecycle, security, recovery, and compatibility without requiring paid cr...

- Requirement: `AGENT-023`
- Section: Testing
- Priority: Medium
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:325`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:326`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-023.

#### Steps

1. Prepare the supported product or operational boundary governed by AGENT-023.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Deterministic tests shall cover providers, lifecycle, security, recovery, and compatibility without requiring paid credentials.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Fixtures cover fake Codex App Server, Codex JSONL, Claude streaming JSON, version/auth failures, permission approve/deny/unsupported, cancellation, timeout, crash, malformed/oversized output, stale envelopes, path escapes, duplicate providers, resume, orphan recovery, redaction, schema v1 compatibility, and all output formats.

### TC-AGENT-024-001: Local smoke tests shall prove installed-client detection without requiring a live model request.

- Requirement: `AGENT-024`
- Section: Delivery
- Priority: Low
- Type: API and contract
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:18`; `change-impact-studio::tests/Cis.Modules.Agent.Tests/AgentExecutionTests.cs:19`

#### Preconditions

The feature build matching sha256:21ec41c20eb1492e75ae3b6435e192b49c6f2b6283732aaa34d1da9f635130b3 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for AGENT-024.

#### Steps

1. Use the supported application or API client to reach the boundary governed by AGENT-024.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Local smoke tests shall prove installed-client detection without requiring a live model request.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Smoke checks may inspect executable version, structured help/protocol initialization, and authentication status only; credential-dependent live calls are opt-in and reported `Unavailable` when not configured, never passed by substitution.
