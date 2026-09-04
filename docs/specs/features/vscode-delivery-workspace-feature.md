---
title: "Visual Studio Code Delivery Workspace"
type: feature-specification
status: Draft
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
source_change: CIS-0001
targets:
  - change-impact-studio
cis:
  stable_id: change-impact-studio:feature:vscode-delivery-workspace
---

# Visual Studio Code delivery workspace

## Intent

Give a human controller a coherent, VS Code-native view of CIS delivery state without
moving planning, analysis, governance, approval, testing, security, or verification
authority out of the CLI and canonical Markdown. The extension should make the next
meaningful action obvious while keeping generated and human-reviewed evidence easy to
inspect. When bounded work is eligible, the same workspace should let the controller
request execution from a discovered Codex or Claude provider through the governed
`cis agent` contract without turning the extension into a chat client or a second agent
orchestrator.

The current VSIX is a technical scaffold. This feature replaces its generic document
tree and command launchers with a designed experience.

## Users and operating context

- **Controller**: initializes or imports repositories, defines change intent, reviews
  impact and plans, and supplies the human decisions that CIS cannot infer.
- **Implementer**: follows an approved plan, opens bounded context, runs workflows, and
  records implementation evidence.
- **Assurer**: reviews designs, tests, security evidence, independent assurance, and
  final completion claims.

One person may hold multiple roles, but the UI must preserve the actor, rationale,
timestamp, authority, and evidence recorded by CIS.

## Functional requirements

| ID | Surface | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
| VSC-001 | contract | not-applicable | The extension shall remain a thin client over stable CIS CLI arguments, exit codes, JSON results, canonical Markdown, and repository-local state. | No graph, planning, approval, testing, security, or verification rule is reimplemented in the extension; every displayed authoritative state identifies its CLI or Markdown source. |
| VSC-002 | frontend | backoffice | An empty or uninitialized workspace shall show a concise native Welcome View. | The view distinguishes no folder, missing CLI, unsupported CLI, uninitialized repository, and initialized repository states and offers only the applicable primary action and documentation links. |
| VSC-003 | frontend | backoffice | The controller shall be able to select the CIS authority repository when more than one VS Code workspace folder is open. | A native Quick Pick lists eligible folders, preserves the explicit selection per workspace, and never silently changes authority when folders are added or removed. |
| VSC-004 | frontend | backoffice | The Workspace view shall summarize repository identity, documentation root, health, graph/index freshness, local AI availability, active change, and blocking review state. | Each summary item has a truthful state, opens its evidence or applicable command, and refreshes without requiring a VS Code reload. |
| VSC-005 | frontend | backoffice | The Changes view shall present current and closed dossiers with phase, lifecycle, stale-state, and blocking-gate information. | Changes are filterable without deep tree nesting; selecting a change opens its overview rather than executing a mutation. |
| VSC-006 | frontend | backoffice | A change overview shall present intent, progress, accepted scope, decisions, tasks, required human review points, and the single next recommended action. | The recommendation is derived from CLI status, explains why it is available or blocked, and never represents a mechanical gate as a new human approval. |
| VSC-007 | frontend | backoffice | Canonical Markdown shall remain directly inspectable and editable through VS Code editors and built-in Markdown preview. | Every summarized requirement, decision, plan, design, test case, and verification claim links to its canonical file; the UI does not maintain a competing editable copy. |
| VSC-008 | frontend | backoffice | The controller shall be able to search bounded context and inspect source evidence and graph relationships. | Search uses native input/Quick Pick behavior, shows truncation and freshness, opens file locations, and loads graph visualization only when explicitly requested. |
| VSC-009 | frontend | backoffice | Plans and tasks shall show category, complexity, dependencies, acceptance criteria, status, owner, and blocking evidence. | Task transitions invoke CIS commands, require rationale only where the CLI requires it, and refresh from canonical state after completion or failure. |
| VSC-010 | frontend | backoffice | Design review shall show textual wireframes, rendered PNGs, provenance, manifest validation, and approve/reject actions at the global design barrier. | Reviewers can compare all declared screen states, enter a rationale, approve or reject through CIS, and cannot start downstream work while the CLI reports the design gate as paused. |
| VSC-011 | frontend | backoffice | Runs shall present workflow, test, coverage, mutation, security, diagnostics, and independent-assurance outcomes without conflating them. | The view preserves first-attempt failures, failure classification, unavailable suites, repository revision, run ID, and artifact hashes; logs and retained artifacts open through bounded CIS retrieval. |
| VSC-012 | frontend | backoffice | Governance shall expose skills, instructions, standards, references, provider selections, and Repository Doctor findings. | Inventories remain compact and searchable; conflicts, quarantines, stale profiles, and suggested fixes are visually distinct from healthy informational state. |
| VSC-013 | frontend | backoffice | Long-running or mutating commands shall have visible progress, cancellation where supported, and inspectable output. | The command is launched as an argument array without a shell; success, warning, failure, timeout, cancellation, and invalid-evidence outcomes remain distinct and the original CLI exit code is retained. |
| VSC-014 | frontend | backoffice | The UI shall refresh deterministically when canonical files or relevant derived manifests change. | File watchers only mark projections stale; an explicit or lifecycle-owned refresh performs one debounced bounded reload, every coalesced caller settles, dependency and secret folders are never read, and a completed foreground run cannot leave polling active. |
| VSC-015 | frontend | backoffice | All views shall support keyboard operation, logical focus restoration, zoom, reduced motion, screen readers, and VS Code light, dark, and high-contrast themes. | Primary journeys complete without a mouse; focus returns to the initiating control after dialogs; status is never communicated by color alone; webview content uses VS Code theme tokens and accessible names. |
| VSC-016 | frontend | backoffice | Loading, empty, partial, unavailable, stale, warning, error, cancelled, and success states shall be explicitly designed. | Every view has bounded recovery guidance; errors disclose no secret values or unrestricted process output and never replace a previous authoritative result with a false success. |
| VSC-017 | security | not-applicable | The extension shall preserve CIS filesystem, process, credential, and evidence trust boundaries. | It reads only selected workspace files and bounded CLI output, never reads credentials or `.env` content, applies workspace containment to opened paths, and requires workspace trust for process execution. |
| VSC-018 | delivery | not-applicable | Installation shall verify compatible VS Code and CIS CLI versions and provide actionable recovery when either component is absent or incompatible. | A packaged VSIX installs on the supported VS Code range, finds `cis` on `PATH` or an explicit executable path, reports the detected versions, and passes clean-profile installation and activation smoke tests. |
| VSC-019 | documentation | not-applicable | User and contributor documentation shall describe installation, first use, navigation, review points, settings, troubleshooting, and the thin-client boundary. | The VSIX README and CIS manual agree with implemented commands and screenshots and contain no unpublished or unsupported workflow claims. |
| VSC-020 | frontend | backoffice | The Runs view shall expose discovered agent providers and truthful provider diagnostics. | Codex and Claude availability, transport, version, authentication, supported run modes, permission capabilities, resume support, and failure reason come from `cis agent providers` or `cis agent provider diagnose`; the extension never reads or manages provider credentials. |
| VSC-021 | frontend | backoffice | An eligible planned task shall offer a governed Request agent work action. | The controller selects only provider, declared run mode, permission ceiling, and target repository values supported by CIS; the extension invokes `cis agent prepare` and `cis agent run` as argument arrays, presents the exact task and ceiling before launch, and displays the precise CIS gate when execution is rejected. |
| VSC-022 | frontend | backoffice | A foreground agent run shall remain observable and controllable from the workspace. | Normalized events show attempt, state, bounded progress, permission requests, isolation mode, provider session reference, cancellation, timeout, failure, and completion without rendering an unrestricted provider transcript; supported cancel, recover, and resume actions invoke CIS and every resume creates a visible new attempt. |
| VSC-023 | frontend | backoffice | Agent results shall remain evidence rather than lifecycle authority. | The run detail opens the bounded result, changed-file inventory, validations, artifacts, hashes, and imported task evidence; importing a result never approves scope, transitions a task, closes a change, or hides the required human review point. |

## Information architecture

Use one **Change Impact Studio** Activity Bar container with no more than five movable
native Views:

1. **Workspace** — identity, health, active change, review gate, and next action.
2. **Changes** — current and closed dossiers with concise lifecycle state.
3. **Evidence** — canonical documents and bounded context search results.
4. **Runs** — agent executions, workflows, tests, security, diagnostics, and retained
   artifacts.
5. **Governance** — skills, standards, references, providers, and doctor findings.

Rich change summaries, relationship graphs, design comparisons, and evidence matrices
may open as editor-area webviews. Webviews are not used for onboarding wizards,
settings, simple lists, notifications, or functionality already supplied by VS Code.

## Required screens and states

The design pack shall cover at least:

- no folder, CLI missing/incompatible, repository uninitialized, and initialization
  collision states;
- healthy workspace, warnings, blocking errors, stale graph/index, and design-review
  pause;
- no changes, active changes, closed changes, filtered results, and stale dossier;
- change overview for proposed, impact review, planning, design review, implementation,
  verification, and closed phases;
- context search empty, matched, truncated, stale, and relationship-detail states;
- plan/task ready, blocked, running, failed, completed, deferred, and exception states;
- provider ready, executable missing, version incompatible, authentication unavailable,
  capability unsupported, and transport unhealthy states;
- agent task preparation, provider/mode/permission selection, launch confirmation,
  running, permission request, cancellation, timeout, crash, recovery, resume, malformed
  result, completion, and imported-evidence states;
- design review with desktop and compact editor widths, rejection, and resubmission;
- test/security run passed, failed, partially unavailable, invalid evidence, and retained
  diagnostic detail;
- governance healthy, conflict, quarantine, missing prerequisite, and suggested-fix
  states.

## Interaction rules

- Clicking an informational tree row selects or opens detail; it does not perform a
  mutation.
- Primary mutations use named commands exposed through toolbars, context menus, or the
  Command Palette.
- Destructive, approval, rejection, exception, and scope-expansion commands show the
  exact target and collect the rationale required by CIS.
- Request agent work shows the exact task, repository, provider, isolation, run mode,
  and permission ceiling. Unsupported combinations are disabled from CIS capability
  evidence rather than guessed by the extension.
- Optional continuation text is transient controller input, is never copied into an
  extension-owned history, and is passed only to `cis agent resume`; the UI warns the
  controller not to enter credentials or secrets.
- Mechanical refreshes and successful deterministic gates do not request approval.
- The UI opens canonical Markdown at the relevant file and, where known, line rather
  than copying long content into notifications.
- The status bar remains compact and workspace-scoped; it shows only the highest
  actionable CIS state and opens Workspace detail.

## Security and privacy

- No credentials, stored provider prompts, `.env` values, unrestricted source bundles,
  or raw tracker bodies are read by the extension. Provider-native authentication stays
  outside the extension.
- Webviews use a restrictive content security policy, nonces for scripts, local resource
  roots, encoded content, and message validation.
- CLI execution is disabled in untrusted workspaces. Read-only Markdown navigation may
  remain available when VS Code permits it.
- Diagnostics shown in notifications are concise and redacted; full bounded evidence is
  opened through the CIS artifact or diagnostics commands.

## Test strategy

- Pure unit tests cover repository selection, path containment, state mapping, command
  construction, refresh debouncing, and view-model projection.
- VS Code integration tests cover activation, Welcome View state, commands, trees,
  editor/webview opening, settings, workspace trust, and multi-root selection.
- Contract fixtures cover supported, missing, incompatible, malformed, partial, stale,
  and oversized CLI JSON responses and exit codes.
- Agent fixtures cover provider diagnosis, ineligible tasks, supported and unsupported
  capability combinations, permission requests, foreground event streaming, cancellation,
  timeout, crash, orphan recovery, resume attempts, result import, redaction, and the rule
  that imported evidence grants no lifecycle authority; no paid provider credential is
  required.
- Accessibility checks cover keyboard journeys, focus order/restoration, semantic
  labels, contrast/theme tokens, high contrast, zoom/reflow, and reduced motion.
- Packaging tests install the VSIX into a clean profile, bind a verified CIS CLI, open
  initialized and uninitialized fixtures, and run Repository Doctor.
- Manual Markdown and CSV cases retain exact `TC-*` identities and are reconciled with
  automated executions before completion can pass.

## Non-goals and explicit exclusions

- Reimplementing the CIS engine in TypeScript or a webview.
- A general project-management board, source-control client, chat interface, agent
  conversation transcript, provider account manager, credential broker, background agent
  scheduler, or detached agent daemon.
- Editing arbitrary `.cis/local/` JSON or SQLite-derived state.
- Automatic approval, rejection, impact disposition, exception creation, or final
  acceptance.
- A browser-only extension in this slice; the CLI requires a local executable and local
  repository filesystem.
- Marketplace publication or making the GitHub repository public before the private
  adoption review is complete.

## Design provenance

The textual wireframes and renderer shall cite the repository's design guidelines and
the official VS Code extension UX guidance used for the design:

- <https://code.visualstudio.com/api/ux-guidelines/overview>
- <https://code.visualstudio.com/api/ux-guidelines/views>
- <https://code.visualstudio.com/api/ux-guidelines/sidebars>
- <https://code.visualstudio.com/api/ux-guidelines/webviews>
