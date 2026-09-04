---
title: "Visual Studio Code Thin Client"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on editor or CLI contract change"
cis:
  stable_id: change-impact-studio:spec:vscode-client
---

# Visual Studio Code thin client

The extension under `vscode-extension/` is presentation and navigation only. It reads the
initialized documentation root, opens canonical Markdown with VS Code's built-in preview,
queries read-only JSON commands, and launches visible CIS tasks for mutating or long-running
commands.

For approved, eligible work, the client may also invoke the stable `cis agent` commands to
diagnose Codex or Claude, prepare a digest-bound task envelope, launch one foreground run
with an explicit mode and permission ceiling, show normalized events and bounded evidence,
and request supported cancel, recover, resume, or result-import operations. Provider
selection, task eligibility, permission enforcement, isolation, event normalization,
redaction, recovery, evidence import, and lifecycle authority remain CIS responsibilities.
The extension does not maintain agent conversation history and does not authenticate to,
store credentials for, or call an agent provider directly.

Before a change exists, the state-driven product-definition projection may invoke `cis
agent author brd` for a generated Review Required BRD. The controller explicitly selects
the reference files and provider, reviews the disclosure and permission boundary, and
receives the protected one-file result in canonical Markdown. The extension never reads,
embeds, copies, or interprets the selected evidence itself and never turns the draft into
approval.

When a backlog item is started, the projection treats the generated file as a scaffold and
offers `cis agent author feature` as the immediate next action. The controller selects and
confirms a workspace-write provider; CIS supplies only the bounded governed product,
technical, architecture, component, UI-direction, standards, security, and testing evidence
inside an isolated scratch repository. The extension does not construct the prompt or apply
the result itself. Existing linked specifications that still contain placeholders expose the
same action instead of a misleading generic Open action.

After an agent-authored draft, the next action is an independent BRD review. The extension
selects only providers with `review` and `read-only` capability, prefers a provider different
from the successful `BRD-DRAFT` run, diagnoses it before execution, and invokes `cis agent
review brd`. Reuse of the extracted authoring references is a separate explicit disclosure
choice. The resulting `.cis/local/agents/runs/<run-id>/brd-review.md` is opened as advisory
evidence; it cannot edit or approve the BRD.
Review completion performs an immediate Workspace refresh followed by one bounded
quiet-period refresh. This absorbs trailing filesystem notifications from the related run and
disposition evidence without reintroducing background polling while an agent is running.

The product journey asks `cis brd review freshness` whether the successful review still
covers the current BRD. Recording stakeholder answers after that review is a compatible
`question-answers-only` change and routes to the remaining questions rather than back to
independent review prematurely. Once all answers exist, the next action is **Update BRD from
answered questions**, which invokes `cis agent incorporate brd-questions` through an
implement-capable provider in its isolated one-file boundary. The applied revision then routes
to a different-provider independent review before approval. A question identity or wording
change, a changed question set, or any other BRD edit also supersedes the review. Raw digest
comparison is only a backward-compatible fallback when the installed CLI does not expose the
governed classification.

Answer-incorporation provenance is transitive across later bounded review-remediation
`BRD-REVISION` runs. The Workspace uses the latest applied `BRD-QUESTION-REVISION` whose answer
digest still matches, even when an approved remediation run is the latest BRD producer. It must
not request another incorporation merely because review findings were applied. A newer BRD
draft or changed governed answer digest invalidates that provenance.

When that review contains recommendations, the next action is Review recommendations.
The extension presents the complete recommendation list in one review surface. The named
human may approve individual recommendations as written, edit and approve exact remediation
text through `cis brd review decide`, or approve every unchanged pending recommendation in
one atomic `cis brd review accept-all` operation. Previously edited approvals remain intact.
The approved text, actor, and timestamp are the decision evidence; no separate acceptance
rationale or aggregate approval is requested. Completion mechanically locks the complete set
and opens agent selection. The extension never chooses or edits a recommendation itself. Approved findings route to
`cis agent revise brd` through a provider different from the reviewer. The revision changes
only the BRD in isolation and then routes directly to `cis agent review brd` through a
provider different from the reviser for closure-only verification. That pass may report only
missed approved scope, introduced rejected scope, revision-caused regression, or protected
evidence/scope drift; it cannot start another broad refinement review. Legacy rejected findings
remain visible guardrails for backward compatibility.

When that draft contains numbered open questions, the next product-definition action is
Answer open BRD questions. The extension opens a consolidated editor page built from `brd
questions guidance`: every question remains visible with deterministic BRD excerpts, current
answer provenance, and any digest-bound advisory suggestion. Editable text areas replace
modal prompts. **Accept suggestion** and **Save answer** are explicit human actions that invoke
the exact `brd questions answer` command with actor provenance. Local suggestions are
generated only when the user selects the page action; remote suggestions require provider selection and a disclosure
confirmation. The extension does not parse the BRD, generate, retain, or silently accept an answer.
After the final answer it does not route directly to validation or approval. It starts the
explicit bounded question-incorporation journey, opens the updated canonical BRD, and then
offers the required independent review. Provider selection and all digest, identity, path,
copyback, and evidence enforcement remain CLI responsibilities.

After BRD approval, the high-level technical-direction page exposes all governed `TI-Q-*`
choices as editable text. Each card has exactly one persistence action: Save for a new answer
or Update for an existing human or derived answer. The textarea content is the exact value sent
to `technical-intent questions answer`; a separate accept-suggestion action is not presented.
Completed questionnaires remain reopenable from both the Workspace and Journey Map. Changing a
recorded choice regenerates the dependent technical intent and component map so they cannot retain
the superseded direction. A successful per-card save remains in the questionnaire and restores the
prior scroll position and focused card after status refresh; it does not open a command-detail editor.
Failure evidence may still open the bounded diagnostic surface.

The extension must not implement graph construction, planning, provider routing, tracker
reconciliation, workflow execution, agent ingestion, verification, approvals, diagnostics,
or learning logic. It passes arguments as a process argument array rather than a shell
string. `cis.executablePath` is a single executable path; users needing a composite launch
must provide a wrapper executable.

The client never reads credentials, `.env` files, stored provider prompts, tracker bodies,
or sensitive diagnostic sources. Optional continuation text is transient controller input
passed only to CIS and is not retained in extension-owned history. CLI exit codes, JSON
output, canonical Markdown, and `.cis/local/` state remain authoritative for display.

## Authority repository

The extension operates against one explicit authority repository per VS Code workspace.

Before authority metadata exists, the Welcome View performs a bounded, read-only evidence scan.
A repository containing recognized project manifests or implementation source is offered
**Import existing repository**, which dry-runs `cis repo import`, confirms the exact in-place
operation, bootstraps that source as authority, and builds context. A genuinely empty project is
offered **Create CIS project** instead. `.git`, generated output, dependencies, documentation,
and CIS metadata alone do not cause an empty project to be treated as an existing implementation.

- With one eligible folder, that folder may be selected automatically and shown as the
  current authority.
- With several eligible folders, a native Quick Pick requires explicit selection. The
  choice is stored in workspace-scoped extension state by stable folder URI.
- Adding, removing, or reordering folders never silently changes an existing authority.
- If the selected folder disappears, all mutations are disabled until the controller
  selects another authority.
- Every path opened by the extension is resolved beneath the selected authority. An
  absolute, escaping, ignored-secret, dependency, or unrestricted `.cis/local/` path is
  rejected before VS Code opens it.

## Information architecture

One `Change Impact Studio` Activity Bar container owns no more than six movable native
Views:

| View | Authoritative sources | Primary role |
|---|---|---|
| Workspace | repository metadata, Doctor, graph/index status, design status, change status | Identity, health, active change, review gate, one next action |
| Journey Map | BRD, technical questionnaire, technical intent, backlog, feature, and change lifecycle status | High-level product definition, technical definition, and repeatable delivery path |
| Changes | `change list/show/status`, `plan show/status` and canonical dossiers | Current and closed change navigation |
| Evidence | canonical Markdown plus bounded `context` and `graph` queries | Document, source-evidence, and relationship navigation |
| Runs | `agent`, `workflow`, `test`, `security`, `diagnostics`, and verification evidence | Distinct provider and execution outcomes |
| Governance | skills, instructions, standards, references, provider diagnostics, Doctor | Searchable governance health and suggested fixes |

Welcome content, Quick Picks, input boxes, progress, notifications, Output, settings, and
simple inventories remain native. Repository Doctor findings, the governed technical questionnaire, change summaries, graph relationships, task details,
design comparison, and run evidence may use editor-area webviews because they require
rich bounded presentation. The high-level product-definition wizard is the one deliberate
onboarding exception: it projects eight CLI-owned pages in one retained panel, saves questionnaire
answers in place, previews derived architecture and visual-system artifacts, supports revision by
direct page navigation, and requests one consolidated activation. It is not a settings page, chat
client, or canonical editor; all lifecycle and content authority remains in CIS and Markdown.

### Repository Doctor detail

Selecting the Workspace health summary or **CIS: Run Repository Doctor** opens one dedicated
editor-area result page, not a flattened JSON table or counter-only tree. It presents repository
and documentation-root identity, local-AI state, separate error/warning/information totals, and
the complete findings grouped by severity. Every finding retains its stable code, category,
message, evidence, suggested fix, fixability classification, and optional CIS command.

Doctor remains read-only. A suggested command is never launched automatically; the page permits
copying the exact current command for review and permits opening only evidence paths that pass
the extension's authority-containment policy. **Run Doctor again** refreshes the existing page and
Workspace projection without opening another editor. Webview messages are allowlisted and a copy
request resolves the command again from the current structured finding rather than trusting text
supplied by the page.

## CLI consumption profile

Every query appends `--repo <authority> --format json`. Every mutation is constructed as
an argument array and invokes an exact documented command. The client never reconstructs
state that CIS can report directly.

| Surface | Read commands | Mutating commands |
|---|---|---|
| Workspace/setup | `repo doctor`, `graph validate`, `change list`, `design status` | `repo init`, `graph build` |
| Changes/tasks | `change list/show/status`, `plan show/status` | `plan task transition` only after exact target/rationale confirmation |
| Evidence | `context search`, `graph related`, canonical file links | None |
| Design review | `design status`, `design validate` | `design approve`, `design reject` with human identity and rationale |
| Runs/providers | `agent providers`, `agent provider diagnose`, `agent runs/show/status`, `test status`, `security status` | `agent author brd`, `agent review brd`, `agent revise brd`, `agent prepare/run/cancel/recover/resume/import-result` |
| Product definition | `definition status`, `brd status`, `brd questions list/guidance`, `brd review status/freshness`, `technical-intent questions status`, UI-direction questions status, technical-intent, solution-design, UI-direction, and backlog status | `definition init/prepare/answer/activate`, `brd review init/decide/accept-all/approve`, `brd questions suggest/answer`, questionnaire answers, validation, and explicit approval commands |
| Governance | inventory/validate/status commands for skills, standards, references and Doctor | Explicit supported audit/fix commands after target confirmation |

The first implementation may expose a subset of these actions, but it must not claim a
surface is healthy, complete, approved, or supported when the corresponding CLI contract
is absent. Unknown fields are ignored for forward compatibility. Missing required fields,
malformed JSON, partial output, oversized output, a missing expected evidence file, and
unsupported CLI versions remain distinct errors.

## Process and result contract

- Queries run with a bounded startup/overall timeout and output limit. A process exit of
  zero does not pass when the expected JSON cannot be read.
- Long-running or mutating commands run in the foreground with visible progress,
  cancellation only where the command declares support, and bounded redacted output.
- The original exit code, stdout/stderr classification, operation/run identity, attempt,
  repository revision, profile digest, and artifact hashes are preserved when returned.
- A failed foreground command presents the bounded CIS diagnostic before provider progress
  telemetry. High-frequency provider protocol events remain in durable run evidence and do
  not flood the notification or conceal the copy-back/application failure.
- A retry or resume is a new visible attempt. It never erases the first failure.
- Loading, empty, partial, unavailable, stale, warning, error, cancelled, invalid-evidence,
  and success are separate view-model states.
- Refresh is debounced. File changes only mark affected projections stale and never run
  CLI projections directly; an explicit or lifecycle-owned refresh performs one bounded
  reload. Every caller coalesced into that reload receives the same completion or failure,
  so a completed foreground operation cannot remain suspended behind a cancelled timer.
  Identical read-only CLI queries share one in-flight result and a five-second projection
  cache. Explicit refresh and every mutation invalidate the cache; failures are never cached.
  Workspace-only status reads use compact command projections, while explicit evidence views
  request full detail.
  Graph, workflow, test, security, agent, and workspace projection commands never run
  continuously from a watcher.

## Agent execution boundary

The Request agent work action is available only for an eligible planned task and values
returned by CIS capability evidence. The confirmation displays change, task, authority
repository, target repository, provider, transport, isolation, mode, permission ceiling,
and approval-request policy. Unsupported combinations are disabled rather than guessed.

Foreground normalized events may show attempt, state, bounded progress, permission
request, session reference, cancellation, timeout, failure, recovery, and completion.
They never expose an unrestricted provider transcript. Importing a result imports evidence
only; it does not approve scope, transition a task, close a change, accept verification, or
hide a required human review.

## Security and privacy controls

- Workspace trust is required before any process is created. Read-only canonical Markdown
  navigation may remain available in an untrusted workspace.
- `execFile`/`ProcessExecution` receives an executable and argument array; a shell string is
  never constructed or evaluated.
- Diagnostics and notifications are bounded and redact likely secrets. Full permitted
  evidence opens through the CIS artifact/diagnostics contract.
- Provider credentials and authentication UI remain provider-native. The extension does
  not inspect environment variables, credential stores, `.env` values, prompts, or tokens.
- Webviews use a nonce, restrictive content-security policy, encoded untrusted values,
  minimal `localResourceRoots`, command allowlists, and schema-validated messages.
- File watchers exclude dependency, generated, secret, and unrestricted local-state paths.

## Installation and compatibility

The VSIX supports the VS Code range declared in `vscode-extension/package.json`. On
activation it resolves `cis.executablePath`, obtains the CLI version without a shell,
and compares it with the extension's declared compatible CLI range. Missing or
incompatible binaries produce actionable Welcome states. A clean-profile smoke test must
install the packaged VSIX, activate it against initialized and uninitialized fixtures, and
exercise Repository Doctor before release.

## Accessibility and themes

All primary journeys work without a mouse. Native controls retain VS Code semantics.
Webviews use VS Code theme/font tokens, semantic headings, landmarks, table/list markup,
accessible names, visible focus, logical focus restoration, reduced motion, 200% zoom and
reflow, and light, dark, and high-contrast support. Status always includes text or an
accessible label and is never encoded by color alone.

## Verification obligations

- Pure unit tests: authority selection, path containment, command construction, result
  classification, projection, redaction, debounce, compatibility, and state mapping.
- Contract fixtures: supported, missing, incompatible, malformed, partial, stale, and
  oversized CLI responses and exit codes.
- VS Code integration tests: activation, Welcome state, six Views, commands, canonical
  editors/previews, workspace trust, settings, multi-root authority, webview messaging,
  focus restoration, and cancellation.
- Agent fixtures: diagnosis, eligibility rejection, capability combinations, permission
  requests, event streaming, cancel, timeout, crash, recovery, resume, malformed result,
  evidence import, redaction, and no lifecycle authority.
- Packaging: build/install a VSIX in a clean profile with a verified CIS CLI and run
  Repository Doctor against initialized and uninitialized repositories.
