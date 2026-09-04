---
title: "CIS-0001 Visual Studio Code Delivery Workspace textual wireframes"
type: textual-wireframes
status: Approved
change_id: CIS-0001
approval_status: Approved
authority: human-reviewed
frontend_type: backoffice
source_feature: "docs/specs/features/vscode-delivery-workspace-feature.md"
---

# Textual wireframes

## Experience contract

The extension is a thin Visual Studio Code client over CIS. One **Change Impact
Studio** Activity Bar container has five movable native Views: **Workspace**,
**Changes**, **Evidence**, **Runs**, and **Governance**. Quick Picks, progress,
notifications, and simple lists use native controls. Rich read-only summaries and
comparisons open in editor-area webviews. Canonical Markdown always opens in a normal
editor or the built-in Markdown preview.

The paths below are stable internal navigation identities. They resolve against the
explicitly selected authority repository and never escape it. Selecting an
informational row does not mutate state. Mutations are named CIS commands, launched as
argument arrays, and preserve the CIS exit code and evidence.

All surfaces use VS Code theme tokens and product icons, support keyboard-only use,
screen readers, zoom/reflow, reduced motion, and light, dark, and high-contrast themes.
Focus returns to the initiating control after dialogs and commands.

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| VSC-WELCOME | backoffice | Welcome and setup | /welcome | Native View | Controller; execution requires trust | Activation, Activity Bar | Distinguish folder, CLI, compatibility, initialization, and collision states. |
| VSC-AUTHORITY-PICKER | backoffice | Authority repository picker | /authority/select | Quick Pick | Controller in multi-root workspace | Workspace toolbar, status item | Explicitly select the one CIS authority repository. |
| VSC-WORKSPACE | backoffice | Workspace overview | /workspace | Native View | Controller, implementer, assurer | Activity Bar, status item | Summarize health, freshness, active change, review gate, and next action. |
| VSC-CHANGES | backoffice | Changes inventory | /changes | Native View | Controller, implementer, assurer | Activity Bar | Filter current and closed dossiers without deep nesting. |
| VSC-CHANGE-OVERVIEW | backoffice | Change overview | /changes/{changeId} | Editor webview | Controller, implementer, assurer | Changes row, active-change row | Show intent, scope, progress, decisions, tasks, reviews, and next action. |
| VSC-EVIDENCE | backoffice | Evidence and context | /evidence | Native View and Quick Pick | Controller, implementer, assurer | Activity Bar, detail links | Open canonical documents and search bounded context. |
| VSC-GRAPH-DETAIL | backoffice | Relationship detail | /evidence/graph/{nodeId} | Editor webview | Controller, implementer, assurer | Explicit relationship action | Inspect bounded source and graph relationships. |
| VSC-TASK-DETAIL | backoffice | Planned task detail | /changes/{changeId}/tasks/{taskId} | Editor webview | Controller, implementer, assurer | Change overview | Show task contract, blockers, evidence, and eligible actions. |
| VSC-DESIGN-REVIEW | backoffice | Design review | /changes/{changeId}/design | Editor webview | Human reviewer | Review gate, task detail | Compare wireframes and PNGs and approve or reject exact evidence. |
| VSC-RUNS | backoffice | Runs and providers | /runs | Native View | Controller, implementer, assurer | Activity Bar | Separate execution outcomes and expose provider readiness. |
| VSC-RUN-DETAIL | backoffice | Run evidence | /runs/{runId} | Editor webview | Controller, implementer, assurer | Runs row, task evidence | Inspect attempts, classifications, events, artifacts, and hashes. |
| VSC-AGENT-REQUEST | backoffice | Request agent work | /changes/{changeId}/tasks/{taskId}/agent-request | Quick Picks and confirmation | Controller with eligible task | Task named action | Select only CIS-supported execution values and confirm bounded work. |
| VSC-AGENT-RUN | backoffice | Foreground agent execution | /runs/{runId}/agent | Progress and editor webview | Controller, implementer | Confirmed request, resume | Observe, cancel, recover, and resume governed work. |
| VSC-GOVERNANCE | backoffice | Governance inventory | /governance | Native View | Controller, implementer, assurer | Activity Bar | Search governance assets, providers, conflicts, quarantine, and Doctor findings. |
| VSC-COMMAND-PROGRESS | backoffice | Command progress and result | /commands/{operationId} | Notification, progress, Output channel | Initiating actor | Named mutation or long query | Preserve progress, cancellation, bounded output, and terminal outcome. |

## Screen definitions

### VSC-WELCOME: Welcome and setup

#### Description

A concise native View shows one state-specific primary action and installation/first-use
help. It never offers speculative setup choices. An untrusted workspace permits
documentation navigation but disables process execution with an explanation.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WELCOME-OPEN-FOLDER | Open Folder | No folder is open | Activate native button | Opens VS Code folder picker; CIS is unchanged | /welcome | VSC-WELCOME | Cancellation leaves state unchanged. |
| WELCOME-CONFIGURE | Configure CIS | CLI is missing or incompatible | Activate button | Opens executable setting and version guidance | /governance | VSC-GOVERNANCE | Invalid path remains visibly incompatible. |
| WELCOME-INIT | Initialize repository | Compatible CLI, trusted folder, uninitialized repository | Confirm documentation root | Runs `cis repo init` | /commands/{operationId} | VSC-COMMAND-PROGRESS | Collision/non-zero exit opens bounded diagnostics. |
| WELCOME-OPEN | Open CIS workspace | Repository initialized | Activate button | Refreshes canonical state | /workspace | VSC-WORKSPACE | Malformed state shows Doctor guidance. |
| WELCOME-HELP | Installation and first use | Always | Activate link | Opens packaged help | /evidence | VSC-EVIDENCE | Missing help reports packaging error. |

#### States

- No folder, CLI missing, CLI incompatible, uninitialized, initialization collision, initialized.
- Untrusted disables execution; loading/cancel/error retains the previous authoritative result.
- Only the action applicable to the current state is primary and keyboard focused.

### VSC-AUTHORITY-PICKER: Authority repository picker

#### Description

A native Quick Pick lists eligible workspace folders with repository name, absolute
folder, documentation root, and health. The explicit selection is workspace-scoped.
Adding or removing a folder never silently changes authority.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AUTHORITY-SELECT | Use as CIS authority | Eligible folder exists | Select a row | Persists folder identity and refreshes CIS | /workspace | VSC-WORKSPACE | Missing/changed folder preserves prior valid selection. |
| AUTHORITY-CLEAR | Clear selection | Selection exists | Activate secondary command | Removes selection without replacement | /welcome | VSC-WELCOME | Settings failure preserves selection. |
| AUTHORITY-CANCEL | Cancel | Picker open | Escape | Makes no change and restores focus | /workspace | VSC-WORKSPACE | Previous authority remains selected. |

#### States

- Zero, one, and multiple eligible folders; removed authority; selection write failure.
- Multiple candidates block mutations until explicit selection.
- Keyboard filter, selected-state announcement, cancellation, and focus restoration.

### VSC-WORKSPACE: Workspace overview

#### Description

Compact rows show repository identity, documentation root, Doctor health, graph/index
freshness, local AI, active change, global review gate, and one CLI-derived next action.
Every state uses icon, word, and accessible description.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WORKSPACE-REFRESH | Refresh | Authority selected | Toolbar command | Debounced bounded refresh | /workspace | VSC-WORKSPACE | Last valid result remains with stale marker. |
| WORKSPACE-EVIDENCE | Open evidence | Row has canonical evidence | Activate row | Opens file or bounded detail | /evidence | VSC-EVIDENCE | Unsafe/missing path is rejected. |
| WORKSPACE-CHANGE | Open active change | Active change exists | Activate row | Opens read-only summary | /changes/{changeId} | VSC-CHANGE-OVERVIEW | Stale dossier offers refresh. |
| WORKSPACE-NEXT | Run next action | CIS declares eligible action | Activate named command | Opens review/task or confirmed command | /changes/{changeId} | VSC-CHANGE-OVERVIEW | Exact CIS gate is shown; no alternate is invented. |
| WORKSPACE-DOCTOR | Run Repository Doctor | Warning or manual request | Activate command | Starts Doctor | /commands/{operationId} | VSC-COMMAND-PROGRESS | Tool failure stays distinct from findings. |

#### States

- Healthy, warning, blocking error; graph current/stale/absent/building/failed.
- Local AI available/unavailable/disabled/diagnosis failure; no active change.
- Review pause names exact artifact; partial data is labeled and never false success.

### VSC-CHANGES: Changes inventory

#### Description

A flat native list groups current and closed dossiers. Filters cover phase, lifecycle,
blocked, and stale state. Rows show ID, title, phase, lifecycle, gate, and freshness.
Selecting a row only opens its overview.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CHANGES-OPEN | Open change | Row selected | Enter/click | Opens summary without mutation | /changes/{changeId} | VSC-CHANGE-OVERVIEW | Missing dossier is labeled stale. |
| CHANGES-FILTER | Filter changes | Inventory loaded | Type/select filter | Updates local projection only | /changes | VSC-CHANGES | Invalid persisted filter resets with notice. |
| CHANGES-CANONICAL | Open dossier index | File exists | Context command | Opens Markdown editor | /evidence | VSC-EVIDENCE | Containment failure is denied. |
| CHANGES-REFRESH | Refresh | Authority selected | Toolbar command | Requeries inventory | /changes | VSC-CHANGES | Existing rows remain stale on failure. |

#### States

- Empty, filtered empty, loading, active, design-paused, implementation, verification, closed.
- Stale rows remain inspectable but clearly identify freshness and recovery.
- Row name, phase, gate, and freshness are announced to screen readers.

### VSC-CHANGE-OVERVIEW: Change overview

#### Description

The editor webview starts with title, lifecycle, source freshness, blocking gate, and one
next action. It summarizes intent, accepted impacts, decisions, plan, tasks, review
points, and canonical links without owning editable lifecycle state.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CHANGE-NEXT | Next recommended action | CIS reports one eligible action | Activate primary button | Opens exact review/task/command | /changes/{changeId}/tasks/{taskId} | VSC-TASK-DETAIL | Blocked action explains gate. |
| CHANGE-TASK | Open task | Task exists | Activate row | Opens detail without transition | /changes/{changeId}/tasks/{taskId} | VSC-TASK-DETAIL | Missing task is stale. |
| CHANGE-DESIGN | Review design | Review is available | Activate review point | Opens comparison | /changes/{changeId}/design | VSC-DESIGN-REVIEW | Invalid manifest disables approval. |
| CHANGE-MARKDOWN | Open canonical file | Contained source exists | Activate link | Opens editor at file/line | /evidence | VSC-EVIDENCE | Unsafe/missing target rejected. |
| CHANGE-REFRESH | Refresh | View active | Toolbar action | Rebuilds projection | /changes/{changeId} | VSC-CHANGE-OVERVIEW | Prior content remains stale on failure. |

#### States

- Proposed, impact, planning, design review, implementation, verification, closed.
- Digest mismatch suppresses mutations; decisions and tasks retain distinct states.
- Human review is visually different from a deterministic gate; compact width is one column.

### VSC-EVIDENCE: Evidence and context

#### Description

The native View groups canonical documents and exposes bounded context search through
native input/Quick Pick. Results show path, excerpt, type, freshness, and truncation.
The extension does not traverse dependency, secret, or unrestricted local-state folders.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| EVIDENCE-OPEN | Open canonical document | Contained result selected | Enter/click | Opens file at location | /evidence | VSC-EVIDENCE | Unsafe/ignored path denied. |
| EVIDENCE-SEARCH | Search context | Index queryable | Enter bounded query | Invokes CIS search | /evidence | VSC-EVIDENCE | Missing/stale index gives build guidance. |
| EVIDENCE-GRAPH | Show relationships | Result has node and user requests graph | Activate context action | Loads bounded neighbors | /evidence/graph/{nodeId} | VSC-GRAPH-DETAIL | Oversized/unsupported graph is explained. |
| EVIDENCE-PREVIEW | Preview Markdown | Markdown selected | Activate preview | Opens built-in preview | /evidence | VSC-EVIDENCE | Source editor remains available. |

#### States

- Empty, matched, truncated, stale, unavailable, malformed, cancelled, no results.
- Result count, repository scope, and truncation limit are announced.
- Existing results remain visible with stale state when refresh fails.

### VSC-GRAPH-DETAIL: Relationship detail

#### Description

The editor webview shows one node, typed incoming/outgoing neighbors, evidence paths,
and graph build identity. Additional neighbors load explicitly. An accessible list is
equivalent to the small visual relationship map.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GRAPH-NODE | Inspect relationship | Neighbor selected | Activate node/list row | Focuses neighbor detail | /evidence/graph/{nodeId} | VSC-GRAPH-DETAIL | Missing node is stale. |
| GRAPH-SOURCE | Open source | Contained source exists | Activate link | Opens file at location | /evidence | VSC-EVIDENCE | Unsafe path denied. |
| GRAPH-MORE | Load more | Result truncated | Activate button | Requests next bounded page | /evidence/graph/{nodeId} | VSC-GRAPH-DETAIL | Current graph remains partial on failure. |

#### States

- Current, stale, truncated, no relationships, source missing, loading, partial, error.
- Keyboard and screen-reader list access matches diagram information.
- Compact widths reflow without horizontal page scrolling.

### VSC-TASK-DETAIL: Planned task detail

#### Description

The editor view shows category, complexity, dependencies, requirements, repositories,
acceptance criteria, status, owner, blockers, evidence, and canonical path. Eligible
transitions and agent actions come from CIS.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TASK-CANONICAL | Open task Markdown | Path contained | Activate link | Opens canonical editor | /evidence | VSC-EVIDENCE | Unsafe/missing path rejected. |
| TASK-TRANSITION | Change task state | CIS reports eligible | Choose transition, provide required rationale, confirm | Invokes exact CIS transition | /commands/{operationId} | VSC-COMMAND-PROGRESS | Gate/conflict does not change local state. |
| TASK-AGENT | Request agent work | Task/provider combination eligible | Activate named action | Opens bounded selection | /changes/{changeId}/tasks/{taskId}/agent-request | VSC-AGENT-REQUEST | Precise CIS gate shown. |
| TASK-EVIDENCE | Open evidence | Evidence exists | Activate row | Opens run/artifact | /runs/{runId} | VSC-RUN-DETAIL | Missing evidence is invalid, never passed. |

#### States

- Draft, ready, blocked, in progress, paused, failed, completed, deferred, exception.
- Dependency/design barriers identify evidence and resolver.
- Rationale appears only when CLI requires it; completion distinguishes evidence states.

### VSC-DESIGN-REVIEW: Design review

#### Description

The editor webview shows guideline and renderer provenance, manifest validation,
textual screens/actions, PNGs grouped by screen/viewport, and rejection history.
Desktop/compact previews compare side-by-side when space permits and stack otherwise.
Approval and rejection require rationale and act on an exact digest.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DESIGN-WIREFRAME | Open wireframes | File exists | Activate link | Opens Markdown | /evidence | VSC-EVIDENCE | Digest mismatch disables approval. |
| DESIGN-PNG | Open full image | Entry validates | Activate preview | Opens image editor | /changes/{changeId}/design | VSC-DESIGN-REVIEW | Missing/hash mismatch is invalid. |
| DESIGN-APPROVE | Approve pack | Validation passes and authority supplied | Enter rationale, confirm digest | Invokes CIS approval and releases barrier | /commands/{operationId} | VSC-COMMAND-PROGRESS | Non-zero exit keeps pause. |
| DESIGN-REJECT | Reject pack | Reviewable pack exists | Enter rationale, confirm | Records rejected digest/history | /commands/{operationId} | VSC-COMMAND-PROGRESS | Missing rationale makes no change. |
| DESIGN-REFRESH | Refresh resubmission | Pack stale/rejected/changed | Activate refresh | Revalidates projection | /changes/{changeId}/design | VSC-DESIGN-REVIEW | Prior review remains visible. |

#### States

- Ready, validation failed, approved, rejected, resubmitted, stale digest.
- Desktop and compact layouts; high contrast and 200% zoom/reflow.
- Missing image/hash, renderer failure, guideline mismatch, and rejection are explicit.
- Downstream tasks remain paused until CIS reports approval.

### VSC-RUNS: Runs and providers

#### Description

The native View separates Agent Providers, Agent Runs, Workflows, Tests, Security,
Diagnostics, and Independent Assurance. Provider rows use CIS evidence for executable,
transport, version, authentication, modes, permissions, resume, and failure reason.
First-attempt failures remain visible beside later attempts.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RUNS-OPEN | Open run | Run selected | Activate row | Opens bounded evidence | /runs/{runId} | VSC-RUN-DETAIL | Missing manifest is invalid evidence. |
| RUNS-DIAGNOSE | Diagnose provider | Provider discovered | Context command | Invokes CIS diagnosis | /commands/{operationId} | VSC-COMMAND-PROGRESS | Failure kinds remain distinct. |
| RUNS-REFRESH | Refresh | Authority selected | Toolbar command | Reads bounded inventories | /runs | VSC-RUNS | Last inventory stays stale. |
| RUNS-ARTIFACT | Open artifact | Contained and policy permits | Activate action | Opens via CIS | /runs/{runId} | VSC-RUN-DETAIL | Hash/path failure is invalid. |

#### States

- Provider ready, missing, incompatible, unauthenticated, unsupported, unhealthy.
- Run queued, running, permission pending, cancelling, cancelled, timed out, crashed,
  recovered, failed, invalid evidence, unavailable, passed.
- Test, coverage, mutation, security, workflow, diagnostics, and assurance stay separate.

### VSC-RUN-DETAIL: Run evidence

#### Description

The editor view identifies run kind/ID, task/change, repository revision, profile
digest, actors, timestamps, attempts, isolation, failure classification, bounded events,
result, changed files, validations, artifacts, and hashes. It never renders an
unrestricted provider transcript or secret-bearing log.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RUN-ARTIFACT | Open artifact | Manifest validates | Activate row | Opens bounded evidence | /runs/{runId} | VSC-RUN-DETAIL | Hash/path failure invalidates evidence. |
| RUN-FILE | Open changed file | Path contained | Activate row | Opens editor/diff | /evidence | VSC-EVIDENCE | Unsafe/absent path denied. |
| RUN-IMPORT | Import task evidence | Completed agent result eligible | Confirm run/task | Invokes evidence import only | /commands/{operationId} | VSC-COMMAND-PROGRESS | Import grants no lifecycle authority. |
| RUN-RESUME | Resume agent run | CIS reports resumable | Optional safe continuation, confirm | Creates visible new attempt | /runs/{runId}/agent | VSC-AGENT-RUN | Prior attempts remain preserved. |
| RUN-RECOVER | Recover interrupted run | Recoverable | Activate action | Invokes deterministic recovery | /commands/{operationId} | VSC-COMMAND-PROGRESS | Ambiguity remains unresolved. |

#### States

- Active, passed, assertion/product failure, infrastructure failure, prerequisite,
  timeout, cancellation, unknown, invalid evidence, unavailable.
- Missing artifacts suppress completion claims; large events are bounded/paged.
- Imported evidence is labeled evidence only, never approval or completion.

### VSC-AGENT-REQUEST: Request agent work

#### Description

Native Quick Picks collect only provider, declared run mode, permission ceiling, and
target repository values supported by CIS. Confirmation states exact change, task,
repository, provider, transport, isolation, mode, ceiling, and approval-request policy.
It warns never to enter credentials or secrets.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AGENT-SELECT | Select execution values | Eligibility/capability evidence current | Complete Quick Picks | Builds a supported argument array | /changes/{changeId}/tasks/{taskId}/agent-request | VSC-AGENT-REQUEST | Unsupported combinations disabled with reason. |
| AGENT-CONFIRM | Start bounded work | Complete, trusted, confirmed | Confirm request | Runs `cis agent prepare` then foreground `cis agent run` | /runs/{runId}/agent | VSC-AGENT-RUN | Exact prepare/run gate shown. |
| AGENT-CANCEL-REQUEST | Cancel | Picker/confirmation open | Escape/Cancel | Discards transient values, restores focus | /changes/{changeId}/tasks/{taskId} | VSC-TASK-DETAIL | No side effect. |

#### States

- No provider, unavailable provider, unsupported combination, stale diagnosis, ready.
- Permission ceilings and isolated-worktree policy are explicit and never expand silently.
- Exact confirmation is readable before Start; untrusted workspace blocks launch.

### VSC-AGENT-RUN: Foreground agent execution

#### Description

Native progress and bounded editor detail show attempt, state, provider session,
isolation, normalized progress, permission requests, elapsed time, and classification.
This is an observable foreground operation, not chat. Resume always creates a new
visible attempt.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AGENT-CANCEL | Cancel run | Supported and active | Confirm target | Invokes `cis agent cancel` | /runs/{runId}/agent | VSC-AGENT-RUN | Failure leaves state truthful and offers recovery. |
| AGENT-PERMISSION | Allow or deny request | Bounded request exposed | Review exact operation, decide | Responds through CIS contract | /runs/{runId}/agent | VSC-AGENT-RUN | Unapproved request remains denied. |
| AGENT-RECOVER | Recover | Orphaned/interrupted and recoverable | Activate action | Invokes recovery | /commands/{operationId} | VSC-COMMAND-PROGRESS | Ambiguity remains unresolved. |
| AGENT-RESUME | Resume as new attempt | Resumable provider reference | Optional safe continuation, confirm | Invokes `cis agent resume` | /runs/{runId}/agent | VSC-AGENT-RUN | Rejection preserves prior evidence. |
| AGENT-RESULT | Open result | Bounded result exists | Activate action | Opens run evidence | /runs/{runId} | VSC-RUN-DETAIL | Malformed/missing is invalid. |

#### States

- Preparing, queued, running, permission requested, cancelling, cancelled, timed out,
  crashed, interrupted, recoverable, resumed, malformed result, failed, completed.
- Idle timeout and recovery are explicit; normalized events are bounded and accessible.
- Completion offers evidence review/import, never transition or approval.

### VSC-GOVERNANCE: Governance inventory

#### Description

The native View groups Skills, Instructions, Standards, References, Agent Providers,
and Repository Doctor. Rows are shallow and searchable and use words plus icons for
healthy, informational, stale, conflict, quarantine, prerequisite, and suggested fix.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GOVERNANCE-OPEN | Open canonical item | Contained source exists | Activate row | Opens file | /evidence | VSC-EVIDENCE | Unsafe/missing path denied. |
| GOVERNANCE-AUDIT | Run audit | Inventory supports it, trusted | Named command | Starts CIS audit | /commands/{operationId} | VSC-COMMAND-PROGRESS | AI availability and audit failure explicit. |
| GOVERNANCE-DOCTOR | Run Doctor | Trusted | Activate command | Runs Doctor and refreshes findings | /commands/{operationId} | VSC-COMMAND-PROGRESS | Tool failure separate from findings. |
| GOVERNANCE-FIX | Apply suggested fix | CIS declares fix, target confirmed | Activate action | Invokes exact `--fix` command | /commands/{operationId} | VSC-COMMAND-PROGRESS | Conflict/quarantine never overwrites silently. |
| GOVERNANCE-PROVIDER | Diagnose provider | Provider discovered | Activate command | Refreshes CIS capability evidence | /commands/{operationId} | VSC-COMMAND-PROGRESS | Credentials never read/requested. |

#### States

- Healthy, conflict, duplicate, quarantined, stale, missing prerequisite, suggested fix.
- Local AI, remote AI, deterministic-only, unavailable, and audit failure.
- Filtered empty offers Clear; no inventory gives initialization guidance.

### VSC-COMMAND-PROGRESS: Command progress and result

#### Description

A VS Code progress notification identifies purpose and target without showing a shell
string. Bounded redacted output streams to the CIS Output channel. Completion preserves
the exit code, classification, duration, operation identity, and evidence links.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| COMMAND-CANCEL | Cancel | Contract supports cancellation | Activate cancel | Requests bounded cancellation | /commands/{operationId} | VSC-COMMAND-PROGRESS | Failed cancellation remains truthful. |
| COMMAND-OUTPUT | Show bounded output | Operation exists | Activate action | Opens CIS Output marker | /commands/{operationId} | VSC-COMMAND-PROGRESS | Oversize output is truncated with evidence. |
| COMMAND-RESULT | Open result | Contained result exists | Activate action | Opens refreshed evidence/view | /workspace | VSC-WORKSPACE | Invalid evidence opens diagnostics, not success. |
| COMMAND-DISMISS | Dismiss | Terminal notification visible | Dismiss | Closes notification only | /workspace | VSC-WORKSPACE | Canonical evidence remains. |

#### States

- Pending, running, cancellation requested, cancelled, success, warning, assertion/product
  failure, infrastructure failure, prerequisite, timeout, invalid evidence, unknown.
- Untrusted workspace denies before process creation.
- Start error, malformed/partial/oversized/stale result remain distinct.

## Journey and requirement coverage

| Requirement | Screens and behavior |
| --- | --- |
| VSC-002 | VSC-WELCOME covers folder, CLI, compatibility, initialization, collision, trust, and ready states. |
| VSC-003 | VSC-AUTHORITY-PICKER requires and persists explicit multi-root authority. |
| VSC-004 | VSC-WORKSPACE exposes identity, health, freshness, local AI, active change, gate, and next action. |
| VSC-005 | VSC-CHANGES is a flat filterable current/closed inventory with stale state. |
| VSC-006 | VSC-CHANGE-OVERVIEW provides scope, progress, decisions, reviews, and one CLI-derived action. |
| VSC-007 | VSC-EVIDENCE and all details open canonical Markdown in VS Code. |
| VSC-008 | VSC-EVIDENCE and VSC-GRAPH-DETAIL cover bounded search, freshness, truncation, and relationships. |
| VSC-009 | VSC-TASK-DETAIL covers task metadata, blockers, criteria, evidence, and CIS-owned transitions. |
| VSC-010 | VSC-DESIGN-REVIEW covers validation, PNGs, provenance, approval, rejection, resubmission, and pause. |
| VSC-011 | VSC-RUNS and VSC-RUN-DETAIL keep workflows, tests, coverage, mutation, security, diagnostics, and assurance distinct. |
| VSC-012 | VSC-GOVERNANCE covers inventories, conflicts, quarantine, providers, Doctor, and fixes. |
| VSC-013 | VSC-COMMAND-PROGRESS preserves argument-array execution, cancellation, output, exit code, and outcomes. |
| VSC-014 | All inventories use debounced bounded refresh, stale labeling, and prior-result preservation. |
| VSC-015 | All screens define keyboard, focus, zoom/reflow, reduced-motion, screen-reader, and theme behavior. |
| VSC-016 | Every screen defines loading, empty/partial, unavailable/stale, error/cancel/success and recovery as applicable. |
| VSC-020 | VSC-RUNS/GOVERNANCE show provider evidence from CIS without credential management. |
| VSC-021 | VSC-TASK-DETAIL/AGENT-REQUEST provide exact capability-bounded preparation and confirmation. |
| VSC-022 | VSC-AGENT-RUN/RUN-DETAIL cover foreground events, permission, isolation, cancel, timeout, crash, recovery, and resume. |
| VSC-023 | VSC-RUN-DETAIL treats results as evidence only; import grants no lifecycle authority. |

## Security and privacy coverage

- Workspace trust is required before process execution.
- Selected authority and opened paths are contained; dependency, credential, secret,
  `.env`, and unrestricted local-state content is not read.
- Commands are argument arrays; output and events are bounded and redacted.
- Webviews use encoded content, restrictive CSP, nonced scripts, declared local resource
  roots, and validated messages.
- Provider-native authentication remains external to the extension.
- Approval, rejection, exception, permission, destructive, and scope-expansion actions
  show exact targets and collect CIS-defined rationale/authority.

## Preserved exclusions

- No CIS engine or authoritative lifecycle model is reimplemented in TypeScript.
- No project board, source-control client, chat/transcript, credential broker, provider
  account manager, detached daemon, or background agent scheduler.
- No arbitrary `.cis/local/` editor or automatic approval, rejection, impact decision,
  exception, task completion, or final acceptance.
- No browser-only extension or marketplace/public-repository release.

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-08-28T16:45:14Z | `sha256:69d6fd6a69eaf1753eadbd74b4da203370ca8f2ee41d3961fb6cdebdba497679` | The VS Code-native five-view workspace, canonical evidence navigation, governed agent execution journey, security boundaries, accessible desktop and compact states, deterministic Sharp/SVG renderer, and 23-PNG manifest are accepted. |
