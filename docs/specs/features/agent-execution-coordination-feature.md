---
title: "Provider-Neutral Agent Execution Coordination"
type: feature-specification
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-08-31"
source_change: CIS-0002
targets:
  - change-impact-studio
cis:
  stable_id: change-impact-studio:feature:agent-execution-coordination
---

# Provider-neutral agent execution coordination

## Intent

Allow a human or thin editor client to request bounded implementation, review, or
planning work from Codex or Claude while CIS remains the authority for workspace
routing, approved scope, task state, evidence, and human decisions. This feature turns
the existing portable-envelope handoff into an observable run lifecycle; it does not
turn an agent provider into a second workflow engine.

This is a prerequisite for the Visual Studio Code delivery workspace in CIS-0001. It
changes the CIS engine and CLI only. It introduces no editor UI, wireframe, visual
design, or marketplace publication work.

## Roles and authority

- **Controller** selects the provider, task, target repository, execution mode, and
  permission ceiling; responds to supported permission requests; and may cancel work.
- **Implementer agent** receives a digest-bound task and bounded context, edits only
  the declared target, runs permitted tools, and returns structured results.
- **Assurer** reviews independent evidence. A provider run cannot act as both
  implementer and assurer unless CIS records a distinct independent technique.
- **CIS** owns routing, lifecycle state, provenance, policy enforcement, result import,
  and verification gates. Canonical Markdown remains authoritative.

Agents never approve a feature, impact, plan, design, exception, or final outcome. A
successful provider process is an implementation claim, not acceptance evidence by
itself.

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
| AGENT-001 | contract | not-applicable | CIS shall discover agent providers through public contracts rather than hard-coded module branches. | Loaded provider assemblies register a stable identifier, display name, transport, executable requirements, supported run modes, permission capabilities, resume behavior, and version diagnostics; duplicate identifiers fail deterministically. |
| AGENT-002 | backend | not-applicable | `cis agent providers` and `cis agent provider diagnose <provider>` shall report truthful provider availability. | Human, JSON, and agent output distinguish executable missing, incompatible version, authentication unavailable, unsupported capability, unhealthy transport, and ready; no token, key, cookie, or credential value is read or emitted. |
| AGENT-003 | contract | not-applicable | The portable envelope contract shall remain available and become the input to direct execution. | Existing envelope schema version 1 remains readable; a new version records task digest, accepted scope digests, context manifest, repository revision or working-tree digest, target repository, run mode, provider, permission ceiling, and expiry without silently weakening old validation. |
| AGENT-004 | backend | not-applicable | `cis agent run <change-id> <task-id>` shall start only an eligible, digest-current task. | CIS rejects unknown, blocked, complete, stale, unapproved-plan, unresolved-impact, ambiguous-target, or scope-conflict tasks before launching a provider; an unapproved design gate blocks downstream work but never the coordination, wireframe, or design task needed to establish that gate. |
| AGENT-005 | backend | not-applicable | Every run shall target an explicitly registered workspace repository and contained working directory. | The target is selected from the task and workspace registry, repository-relative paths are containment checked, multi-repository tasks require an explicit target per execution, and neither current-directory coincidence nor provider output can change authority. |
| AGENT-006 | security | not-applicable | The controller shall set an execution mode and permission ceiling before launch. | Modes are `plan`, `implement`, and `review`; permission ceilings are `read-only` and `workspace-write`; network, secret access, unrestricted host access, and destructive operations are never inferred from task prose. |
| AGENT-007 | backend | not-applicable | A run shall expose a durable state machine and append-only event stream. | States include `Prepared`, `Starting`, `Running`, `AwaitingPermission`, `Cancelling`, `Cancelled`, `Succeeded`, `Failed`, `TimedOut`, `InvalidEvidence`, and `Interrupted`; events have monotonic sequence, UTC timestamp, attempt, kind, redacted payload, and provider correlation identity. |
| AGENT-008 | contract | not-applicable | Codex support shall use the supported rich-client boundary for stateful execution. | The Codex adapter can start and communicate with Codex App Server over structured messages, records server and protocol versions, streams thread/turn/item events, maps approval requests into CIS permission events, and retains the Codex thread identity for supported continuation. |
| AGENT-009 | contract | not-applicable | Codex non-interactive execution shall remain available as an explicit bounded fallback. | The adapter can invoke `codex exec --json` using an argument array, parse and bound JSONL events, set approval policy to `never` unless the controller explicitly authorized automatic review within the workspace-write ceiling, preserve the session identity needed for supported resume, and never silently substitute fallback for a requested App Server capability. |
| AGENT-010 | contract | not-applicable | Claude support shall use a supported headless or SDK boundary without parsing terminal presentation text. | The Claude adapter invokes structured print-mode or SDK execution, consumes JSON or streaming JSON, records CLI/SDK version and session identity, applies declared tool and permission settings, and reports unsupported interactive permission or resume capabilities rather than inventing parity. |
| AGENT-011 | security | not-applicable | Provider permission requests shall be mediated by CIS capability and policy. | A supported request records provider, run, attempt, requested capability, bounded target, rationale, response actor, response, and UTC timestamp; a request above the declared ceiling is denied; non-interactive transports fail as `permission-required` without retrying more permissively. |
| AGENT-012 | backend | not-applicable | `cis agent runs`, `show`, `cancel`, and `resume` shall manage observable runs without rewriting history. | Listing and detail are derived from durable state; cancellation terminates the owned process tree and remains distinguishable from failure; resume creates a new attempt linked to the same CIS run, resends the complete digest-bound contract before any optional continuation, and uses provider continuation only when its recorded capability supports it. |
| AGENT-013 | backend | not-applicable | Timeouts, interruption, and stale local locks shall have deterministic recovery. | Each run has bounded startup, idle, and total timeouts; one active run per task and target is the default; orphaned process identities are diagnosed before lock recovery; a retry never erases the first attempt or its failure classification. |
| AGENT-014 | contract | not-applicable | Provider output shall be reconciled into the existing structured result and task-evidence model. | CIS validates envelope identity and freshness, changed-file containment, declared target, summary, validations, artifacts, and terminal status; unreadable, missing, stale, or partial expected results become `InvalidEvidence`, never `Succeeded`. |
| AGENT-015 | backend | not-applicable | Canonical task state shall change only through an explicit CIS reconciliation step. | A provider cannot write approval or lifecycle evidence directly; imported results append provenance to the task and may support a later governed transition, but do not automatically mark the task or change complete. |
| AGENT-016 | backend | not-applicable | Derived run state shall live below `.cis/local/agents/runs/<run-id>/`. | Each run retains a manifest, append-only events, attempts, permission records, bounded stdout/stderr or protocol logs, result, artifact inventory, and digests; canonical documents store only reviewed summaries and durable evidence references. |
| AGENT-017 | backend | not-applicable | Agent runs shall participate in CIS diagnostics, feedback, and cost accounting. | Tool calls, durations, exit/failure classifications, provider-reported token usage and cost when available, estimated context/token savings, redaction outcomes, and artifact hashes use existing CIS evidence contracts and remain attributable to run and attempt. |
| AGENT-018 | security | not-applicable | Credentials, provider configuration, prompts, output, and logs shall preserve repository privacy. | CIS relies on provider-native authenticated sessions or named environment references, never persists secret values, redacts configured patterns before persistence or display, bounds log and artifact size, and excludes secret/dependency paths from context packaging. |
| AGENT-019 | security | not-applicable | Process execution shall avoid shell interpolation and untrusted executable substitution. | Executables are resolved from an explicit absolute override, direct executable discovery on the process PATH, or a bounded platform-specific installed-client location; arguments use process argument lists, working directories are contained, inherited environment is allowlisted where required, process identity and executable digest are recorded, and output cannot be interpreted as CIS commands. |
| AGENT-020 | governance | not-applicable | Workspace policy shall define provider defaults and isolation without making a vendor mandatory. | No provider is selected silently unless a canonical provider profile declares the default; Git repositories default to an isolated worktree for implementation runs; a dirty source is represented by a private snapshot commit containing the exact tracked and non-ignored untracked baseline without moving refs or changing the real index; direct dirty-working-tree execution requires an explicit profile policy and records a pre-run snapshot. |
| AGENT-021 | delivery | not-applicable | Repository initialization shall seed provider-neutral instructions, skills, and a reference profile idempotently. | Reinitialization preserves reviewed policy; agents are instructed to run `repo init`, use Repository Doctor after failure, obey approved scope and design barriers, avoid lifecycle authority, and reconcile results; Doctor reports missing profile, unavailable declared default, incompatible executable, stale run, orphaned process, and unsafe policy. |
| AGENT-022 | contract | not-applicable | All agent commands shall provide stable human, JSON, and agent output suitable for a thin client. | JSON uses versioned contracts and stable status/failure codes; human output is concise; agent output is single-record oriented; provider-native events remain encapsulated behind normalized CIS event kinds with an inspectable bounded raw reference. |
| AGENT-023 | testing | not-applicable | Deterministic tests shall cover providers, lifecycle, security, recovery, and compatibility without requiring paid credentials. | Fixtures cover fake Codex App Server, Codex JSONL, Claude streaming JSON, version/auth failures, permission approve/deny/unsupported, cancellation, timeout, crash, malformed/oversized output, stale envelopes, path escapes, duplicate providers, resume, orphan recovery, redaction, schema v1 compatibility, and all output formats. |
| AGENT-024 | delivery | not-applicable | Local smoke tests shall prove installed-client detection without requiring a live model request, diagnose authentication uncertainty without falsely blocking a usable ambient session, and keep explicit setup provider-native. | Smoke checks may inspect executable version, structured help/protocol initialization, and authentication status only; Codex resolution checks an explicit `CIS_CODEX_EXECUTABLE`, the process PATH, then the newest bounded Windows Codex Desktop installation. Doctor inspects enabled direct providers; an inconclusive Codex login-status probe is informational and execution remains eligible. `cis agent provider authenticate` launches only declared provider-native methods, never accepts credentials, and re-diagnoses after completion. Credential-dependent live calls are opt-in and reported `Unavailable` when not configured, never passed by substitution. |
| AGENT-025 | backend | not-applicable | A controller may assign pre-change BRD drafting from explicitly selected reference evidence without fabricating a planned delivery task. | `cis agent author brd` accepts bounded non-sensitive plain-text and Word Open XML (`.docx`) references, extracts Word text without executing or forwarding embedded content, records the original source digests plus extraction provenance, runs in a minimal isolated scratch repository, applies only the canonical Review Required BRD when its original digest is current and the exact diff changes no other file, preserves frontmatter and every CIS-managed block, records `PRODUCT/BRD-DRAFT` run provenance, and grants no validation or approval authority. |
| AGENT-026 | assurance | not-applicable | A controller may assign the generated BRD to a different provider for independent review before stakeholder resolution and approval. | `cis agent review brd` requires review mode and a read-only isolated scratch repository, rejects the latest successful authoring provider, optionally includes the digest-matching extracted authoring evidence only after explicit disclosure authorization, invalidates any file mutation or malformed review, retains structured `ready`, `revise`, or `blocked` findings plus human-readable Markdown under the `PRODUCT/BRD-REVIEW` run, and grants no validation, answer, source-assessment, or approval authority. |
| AGENT-027 | governance | not-applicable | A human can disposition the complete independent-review recommendation list without granting the reviewing or revising agent decision authority. | `cis brd review init/status/decide/accept-all` creates canonical Markdown bound to the exact review result and BRD digest, presents all findings together, lets a named human approve recommendations individually as written, edit and approve exact remediation text, or atomically approve every unchanged pending recommendation while preserving prior edits, and locks the complete set by decision digest without another aggregate approval; `approve` remains legacy recovery and rationale-backed legacy rejections remain readable as guardrails. |
| AGENT-028 | backend | not-applicable | Recommendations can be approved as written or edited and approved, then applied by another agent and checked by a secondary independent verifier. | `cis agent revise brd` requires an approved set with exact approved recommendation text and a provider different from the source reviewer, runs in an isolated one-file workspace, requires exact approved finding identities in structured completion, preserves frontmatter, human answers, managed baseline/traceability, and every source identity/hash/assessment/provenance field while allowing only explicitly approved source-rationale remediation, records application provenance, reuses an unchanged successful retained revision after copy-back rejection without another provider call, and makes a subsequent `cis agent review brd` require a provider different from the reviser, include the applied disposition record, and perform closure-only verification without reopening broad completeness or unrelated refinement. |

## CLI contract

```text
cis agent providers
cis agent provider diagnose <provider>
cis agent prepare <change-id> <task-id> [--provider <provider>]
cis agent run <change-id> <task-id> --provider <provider> --mode <mode> --permission <ceiling> [--target <repository-id>] [--transport <transport>]
cis agent author brd --reference <reference-file>... --provider <provider> --actor <human> [--transport <transport>]
cis agent review brd --provider <different-provider> --actor <human> [--transport <transport>] [--include-authoring-evidence]
cis brd review init|status <review-run-id>
cis brd review decide <review-run-id> <finding-id> --decision accepted --actor <human> [--approved-recommendation <exact-text>]
cis brd review approve <review-run-id> --reviewer <human> --reason <rationale>
cis agent revise brd --review <review-run-id> --provider <different-provider> --actor <human> [--transport <transport>]
cis agent runs [--change <change-id>] [--task <task-id>] [--status <status>]
cis agent show <run-id>
cis agent cancel <run-id> --actor <actor> --reason <reason>
cis agent resume <run-id> [--message <message>] --actor <actor> --reason <reason>
cis agent import-result <envelope> --result <result>
cis agent status
```

`run` is foreground and streams normalized events by default. A future editor client
owns that CLI process and cancellation. Detached local daemons, cloud scheduling, and
remote agent farms are excluded from this slice. If the client disconnects, CIS records
the interruption and a later explicit resume may create another attempt.

## Provider architecture

- Provider-neutral contracts live in `Cis.Abstractions`.
- Run lifecycle, policy, persistence, reconciliation, and commands live in
  `Cis.Modules.Agent`.
- Codex transport code lives in `Cis.Providers.Agent.Codex`.
- Claude transport code lives in `Cis.Providers.Agent.Claude`.
- Provider assemblies do not reference planning, verification, design, or editor code.
- The portable provider remains a preparation-only provider for humans, other tools,
  and forward compatibility.

The first provider implementation shall prefer Codex App Server for stateful execution
because it is the supported rich-client interface. Codex JSONL execution remains a
declared batch fallback. Claude shall use its structured headless or SDK contract;
terminal UI scraping is prohibited.

## Run provenance and storage

The run manifest records at minimum:

- CIS and provider contract versions;
- run, attempt, change, task, envelope, provider, transport, and provider-session IDs;
- authority and target repository IDs, absolute target resolved at runtime, working
  directory relative to the target, Git revision, dirty state, and snapshot digest;
- accepted feature, impact, plan, decision, design, task, context, skill, instruction,
  standard, and provider-policy digests applicable to the run;
- execution mode, permission ceiling, timeouts, executable path digest and version;
- start/end timestamps, terminal status, failure classification, result digest,
  artifact hashes, and reported usage.

Logs are derived, bounded evidence. They may be retained, compacted, or removed under
the local-artifact policy while their inventory and hashes remain traceable. Secret
values and unbounded raw prompts are not promoted into canonical Markdown.

## Security boundaries

- Direct execution is disabled in untrusted or unresolved repositories.
- A provider receives only the selected context pack and task contract; CIS does not
  dump the entire graph, `.cis/local/`, credentials, dependency folders, or unrelated
  repository content into the prompt.
- `workspace-write` permits edits only inside the selected target/worktree. It does not
  imply network, administrative, credential, publish, push, merge, deployment, or
  destructive authority.
- Provider process descendants are owned and cancelled as one bounded process tree.
- Structured provider events are data. They cannot invoke CIS commands or grant
  permission without a validated controller action.

## Test strategy

- Contract tests validate provider discovery, stable capabilities, schema evolution,
  event normalization, result mapping, and backward-compatible portable envelopes.
- Lifecycle tests use deterministic fake processes/protocol servers and a controllable
  clock to cover every state transition, timeout, interruption, resume, lock, and
  recovery path.
- Security tests cover path containment, argument construction, environment filtering,
  redaction, output limits, credential non-persistence, request-above-ceiling denial,
  untrusted repository rejection, and process-tree cancellation.
- Provider adapter fixtures replay official structured Codex and Claude message shapes;
  malformed, unknown, duplicated, stale, and oversized messages remain safe.
- Initialization, Doctor, manuals, skills, instructions, package, Linux, and installed
  executable detection are included in the normal CIS verification chain.
- Live Codex or Claude model calls are opt-in smoke tests and are not required for
  deterministic completion.

## Non-goals and explicit exclusions

- Implementing the CIS-0001 Visual Studio Code UI, chat transcript, or editor commands.
- Replacing provider-native model selection, authentication, or account management.
- Storing API keys, OAuth tokens, provider cookies, or secret values in CIS Markdown or
  derived state.
- Autonomous plan, impact, design, exception, scope-expansion, or completion approval.
- Detached daemons, remote worker queues, cloud agent scheduling, or multi-host session
  migration.
- Treating vendor token/cost reporting as billing authority when a provider does not
  supply it.
- Parsing decorated terminal output or directly integrating with another editor
  extension's private APIs.

## External contract provenance

The Codex boundary is based on the official App Server and non-interactive execution
contracts:

- <https://learn.chatgpt.com/docs/app-server>
- <https://developers.openai.com/codex/non-interactive-mode>

The Claude boundary shall be verified against the official Claude Code CLI and Agent
SDK documentation during adapter implementation. Provider-specific behavior remains
behind capabilities so later vendor changes do not alter canonical CIS authority.
