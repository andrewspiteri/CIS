---
title: "CIS-0001 design approval"
type: design-approval
status: Approved
change_id: CIS-0001
approval_status: Approved
gate_status: Approved
authority: human-reviewed
---

# Design approval

For UI-bearing work, a completed renderer and PNG pack set `gate_status` to
`PausedForReview`. All non-review work stops until an explicit approval sets the gate
to `Approved`. Rejection keeps the global pause active and permits only wireframe and
design revision.

## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `docs/changes/CIS-0001/wireframes.md` | `sha256:69d6fd6a69eaf1753eadbd74b4da203370ca8f2ee41d3961fb6cdebdba497679` | `docs/specs/design-guidelines.md` | `sha256:972add8e8d0bec8cb1008c02f0b672f3b838d862c8e1e3f40563a6fbed450f1a` | `docs/changes/CIS-0001/assets/render-vscode-delivery-workspace-screens.mjs` | `sha256:2beb47ef4cf0882016b2655c3bcb74c96a9e676abfdb43924f694777434b9663` | v24.15.0 | 0.35.4 | 8.18.6 |
## Application shell and component templates

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |
| `shell.standard-app` | 1.0 | shell | CIS built-in | Dark application rail, top bar, route context, and governed content canvas. | Selected |
| `component.page-header` | 1.0 | component | CIS built-in | Consistent title, description, status, and primary action hierarchy. | Selected |
| `component.button` | 1.0 | component | CIS built-in | Primary, secondary, destructive, icon, disabled, loading, and focus-visible button states. | Selected |
| `component.text-input` | 1.0 | component | CIS built-in | Labeled text input with helper, placeholder, required, focus, disabled, and validation states. | Selected |
| `component.select` | 1.0 | component | CIS built-in | Labeled select/dropdown with placeholder, selected, open, disabled, and validation states. | Selected |
| `component.textarea` | 1.0 | component | CIS built-in | Multiline input with label, helper, character count, resize affordance, and validation states. | Selected |
| `component.checkbox` | 1.0 | component | CIS built-in | Checkbox with checked, unchecked, indeterminate, focus, disabled, and error states. | Selected |
| `component.tabs` | 1.0 | component | CIS built-in | Route-aware tabs with active, hover, focus, disabled, and overflow behavior. | Selected |
| `component.breadcrumbs` | 1.0 | component | CIS built-in | Consistent hierarchical path, current-page semantics, and truncation behavior. | Selected |
| `component.dropdown-menu` | 1.0 | component | CIS built-in | Anchored action menu with groups, destructive separation, keyboard focus, and disabled items. | Selected |
| `component.dialog` | 1.0 | component | CIS built-in | Modal confirmation/form shell with title, description, focus boundary, actions, and destructive variant. | Selected |
| `component.alert` | 1.0 | component | CIS built-in | Inline/banner feedback for information, success, warning, error, denied, and recovery actions. | Selected |
| `component.table` | 1.0 | component | CIS built-in | Reusable table header, rows, status cells, empty state, and row action affordance. | Selected |
| `component.filter-bar` | 1.0 | component | CIS built-in | Search, filters, applied-filter chips, and result count. | Selected |
| `component.status-badge` | 1.0 | component | CIS built-in | Guideline-bound success, warning, critical, neutral, and accent status pills. | Selected |
| `component.card` | 1.0 | component | CIS built-in | Standard bordered content card with restrained radius and hierarchy. | Selected |
| `component.form` | 1.0 | component | CIS built-in | Labeled fields, helper text, validation, and action row. | Selected |
| `component.empty-state` | 1.0 | component | CIS built-in | Consistent empty, denied, error, and recovery-state composition. | Selected |
| `component.timeline` | 1.0 | component | CIS built-in | Audit/activity timeline with status-aware markers and metadata. | Selected |
| `component.accordion` | 1.0 | component | CIS built-in | Expandable content groups with open, closed, focus, disabled, and nested-content behavior. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `VSC-WELCOME`, `VSC-AUTHORITY-PICKER`, `VSC-WORKSPACE`, `VSC-CHANGES`, `VSC-CHANGE-OVERVIEW`, `VSC-EVIDENCE`, `VSC-GRAPH-DETAIL`, `VSC-TASK-DETAIL`, `VSC-DESIGN-REVIEW`, `VSC-RUNS`, `VSC-RUN-DETAIL`, `VSC-AGENT-REQUEST`, `VSC-AGENT-RUN`, `VSC-GOVERNANCE`, `VSC-COMMAND-PROGRESS` | `shell.standard-app` | VSC-WELCOME:Render required, VSC-AUTHORITY-PICKER:Render required, VSC-WORKSPACE:Render required, VSC-CHANGES:Render required, VSC-CHANGE-OVERVIEW:Render required, VSC-EVIDENCE:Render required, VSC-GRAPH-DETAIL:Render required, VSC-TASK-DETAIL:Render required, VSC-DESIGN-REVIEW:Render required, VSC-RUNS:Render required, VSC-RUN-DETAIL:Render required, VSC-AGENT-REQUEST:Render required, VSC-AGENT-RUN:Render required, VSC-GOVERNANCE:Render required, VSC-COMMAND-PROGRESS:Render required |
## Required states

- [x] Primary flow — healthy Workspace, active Changes, planning overview, ready task,
      evidence search, graph detail, and governed agent confirmation.
- [x] Empty state — filtered-empty Changes and no-folder setup state.
- [x] Loading state — running command progress with bounded Output evidence.
- [x] Error state — initialization collision, failed first attempts, rejection, provider
      unavailable, and invalid-evidence recovery.
- [x] Permission-denied state — untrusted workspace and governed agent permission request.
- [x] Responsive states — desktop `1600x1000` and compact `1100x760` layouts.
- [x] Relevant workflow and lifecycle states — planning, design pause, ready, blocked,
      rejected, running, recovered, failed, completed, stale/partial, and conflict.
- [x] Accessibility intent — word-and-icon status, logical document order, visible focus,
      bounded announcements, equivalent graph list, reflow, and focus restoration are
      explicit in `wireframes.md` and preserved by the native-control mapping.

## Reused approved PNGs

These exact PNGs remain owned by their approved source change. CIS verifies the source
approval digests and current file hashes before rendering or approving this pack.

| Target screen ID | Frontend type | Source change | Source screen ID | State | Viewport | Path | Compatibility | Dimensions | SHA-256 | Source wireframe approval SHA-256 | Source wireframe content SHA-256 | Source renderer SHA-256 | Source manifest SHA-256 | Reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |

## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `vsc-agent-request` | `backoffice` | confirmation | desktop | `docs/changes/CIS-0001/assets/vsc-agent-request--confirmation--desktop.png` | 1600x1000 | `sha256:58c779539d953202485802195a2b445d3066ce1a054e4ff28440c42c2969710c` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-agent-run` | `backoffice` | permission-request | desktop | `docs/changes/CIS-0001/assets/vsc-agent-run--permission-request--desktop.png` | 1600x1000 | `sha256:58fd05989d9a2d79a6fbc4cb0f45b9055b7fba8ca1e021ab77c6c385c003e8d1` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-agent-run` | `backoffice` | recovered | compact | `docs/changes/CIS-0001/assets/vsc-agent-run--recovered--compact.png` | 1100x760 | `sha256:b56004de608a2257706dd36245fe7fdb4bdc356de7aa0dd75800a1476051e1c9` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-authority-picker` | `backoffice` | multi-root | compact | `docs/changes/CIS-0001/assets/vsc-authority-picker--multi-root--compact.png` | 1100x760 | `sha256:1fdaa7ef587475ecc2b61d1c10df495d6d6e67ecae4a269ef7d04ebd2d5df376` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-change-overview` | `backoffice` | design-review | compact | `docs/changes/CIS-0001/assets/vsc-change-overview--design-review--compact.png` | 1100x760 | `sha256:be71b34e6250543e5b1a0b2141e3b07e891a92c15a0fd4e9d59a4f937f76834c` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-change-overview` | `backoffice` | planning | desktop | `docs/changes/CIS-0001/assets/vsc-change-overview--planning--desktop.png` | 1600x1000 | `sha256:3822814d15276b597e7dd645a637ae504b3940f0c3cc82f977e74871e3866712` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-changes` | `backoffice` | active | desktop | `docs/changes/CIS-0001/assets/vsc-changes--active--desktop.png` | 1600x1000 | `sha256:8eefce4c90983e0b931d7cf0ce053a443bb10efbc5984f95c9434db4aae523f1` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-changes` | `backoffice` | filtered-empty | compact | `docs/changes/CIS-0001/assets/vsc-changes--filtered-empty--compact.png` | 1100x760 | `sha256:dffe3570f54c0bb96eb823c52c3c2696dca58102c65bd9bb2278251ab6b363d7` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-command-progress` | `backoffice` | invalid-evidence | compact | `docs/changes/CIS-0001/assets/vsc-command-progress--invalid-evidence--compact.png` | 1100x760 | `sha256:f88bc1f8c6c43411f3c5a0a11cb96e5065ae2183f0f26edfeee2ff6b072ec264` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-command-progress` | `backoffice` | running | desktop | `docs/changes/CIS-0001/assets/vsc-command-progress--running--desktop.png` | 1600x1000 | `sha256:c1440aca8b2c88023784be57c64298b031ab8217bc28a0ae16cdb80f597e3faa` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-design-review` | `backoffice` | ready | desktop | `docs/changes/CIS-0001/assets/vsc-design-review--ready--desktop.png` | 1600x1000 | `sha256:3ed121f2088815e1880a189aba399a270f543d084f690897372a012a572d04ff` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-design-review` | `backoffice` | rejected | compact | `docs/changes/CIS-0001/assets/vsc-design-review--rejected--compact.png` | 1100x760 | `sha256:b6cea21cfc9473876a46c13f4dbaf78cf9c21900e2b205b06c5c067a0616264d` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-evidence` | `backoffice` | matched | desktop | `docs/changes/CIS-0001/assets/vsc-evidence--matched--desktop.png` | 1600x1000 | `sha256:356a98cdc5229fb08186f8a65bfb031068ea6e6bddcd9a0e956c0a1605154156` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-governance` | `backoffice` | conflicts | desktop | `docs/changes/CIS-0001/assets/vsc-governance--conflicts--desktop.png` | 1600x1000 | `sha256:f59c5ea8b68a10ae2c55c39b040b89b153ec6e5594931d1ca71a3d5e4682e50d` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-graph-detail` | `backoffice` | truncated | desktop | `docs/changes/CIS-0001/assets/vsc-graph-detail--truncated--desktop.png` | 1600x1000 | `sha256:938ece04baea92e315da1e27d04a84bcaab6f9cf5aa95087469b7a18f1fadd50` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-run-detail` | `backoffice` | failed-attempt | desktop | `docs/changes/CIS-0001/assets/vsc-run-detail--failed-attempt--desktop.png` | 1600x1000 | `sha256:f69485fee850c5f3474825433da2bee82049223edc849fd66470714e54bc1bfe` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-runs` | `backoffice` | mixed-outcomes | desktop | `docs/changes/CIS-0001/assets/vsc-runs--mixed-outcomes--desktop.png` | 1600x1000 | `sha256:0b747bd04cff61777031f47ad2441116043212b2cfdcba553f6f4a826fb29561` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-task-detail` | `backoffice` | blocked | compact | `docs/changes/CIS-0001/assets/vsc-task-detail--blocked--compact.png` | 1100x760 | `sha256:44d9145503e492fab59f9d62e09786444be3cc52820f6c231e0d97feec5c5d3e` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-task-detail` | `backoffice` | ready | desktop | `docs/changes/CIS-0001/assets/vsc-task-detail--ready--desktop.png` | 1600x1000 | `sha256:6a3e989c06848191b9028c7a6866feaa50d8ebaa9d20ac3e2e17d9f898d50f54` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-welcome` | `backoffice` | setup-states | desktop | `docs/changes/CIS-0001/assets/vsc-welcome--setup-states--desktop.png` | 1600x1000 | `sha256:503094bee475a1e1fd4711c35711a02e999862d17d7411809d34e4421515ae84` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-welcome` | `backoffice` | untrusted | compact | `docs/changes/CIS-0001/assets/vsc-welcome--untrusted--compact.png` | 1100x760 | `sha256:cd20d3078e93ff5a3d91572d5f07f96e97b4d045a8049e642790c072b7021b62` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-workspace` | `backoffice` | healthy | desktop | `docs/changes/CIS-0001/assets/vsc-workspace--healthy--desktop.png` | 1600x1000 | `sha256:6c6d347ab7db494938958859d239c044e29823f00c270daccd3940f48d45ca0d` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `vsc-workspace` | `backoffice` | review-paused | compact | `docs/changes/CIS-0001/assets/vsc-workspace--review-paused--compact.png` | 1100x760 | `sha256:9f0a1e3d5a27f4b126afacd5ba95ab63657ed320464df1591191eda44dbddffa` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
## Guideline conformance and deviations

| Rule/token | Renderer mapping | Result | Deviation approval |
| --- | --- | --- | --- |
| Repository design guidelines | `docs/specs/design-guidelines.md` at `sha256:972add8e8d0bec8cb1008c02f0b672f3b838d862c8e1e3f40563a6fbed450f1a` | Passed | None |
| VS Code UX overview | <https://code.visualstudio.com/api/ux-guidelines/overview> reviewed 2026-08-28 | One Activity Bar container, native Views/Quick Picks/notifications, editor webviews for rich detail | None |
| VS Code Views guidance | <https://code.visualstudio.com/api/ux-guidelines/views> reviewed 2026-08-28 | Five shallow Views, product-style icons, informational rows open detail rather than firing mutations | None |
| VS Code Sidebars guidance | <https://code.visualstudio.com/api/ux-guidelines/sidebars> reviewed 2026-08-28 | One container and five Views; compact toolbars and no duplicated editor functionality | None |
| VS Code Webviews guidance | <https://code.visualstudio.com/api/ux-guidelines/webviews> reviewed 2026-08-28 | Webviews limited to rich summaries, graph, design comparison, and evidence matrices; setup and selectors remain native | None |
| Neutral/accent palette and 8 px rhythm | Renderer `c` tokens, 14/28/48 px compositions, restrained blue/teal/status colors | Passed; no state is color-only | None |
| Shared shell and controls | `shell.standard-app@1.0` and 19 transitive component templates map to reusable renderer helpers | Passed; no screen-specific control redraw | None |
| Offline, deterministic evidence | Self-contained JavaScript-generated SVG; Sharp 0.35.4 and libvips 8.18.6; no network assets | Passed | None |
| Native application shell | Standard shell semantics are specialized to VS Code title bar, Activity Bar, movable Views, editor tabs, Output, and status bar | Intentional native-platform adaptation | Included in the combined design decision |
| Typography | Native surfaces and webviews use VS Code/host font tokens represented by `Inter, Segoe UI, Arial` fallback in the renderer rather than embedding a competing font | Intentional native-platform adaptation | Included in the combined design decision |

## Rejected revisions

Rejected PNG files are removed. Preserve their manifest hashes and review evidence.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-08-28T16:45:14Z | `sha256:69d6fd6a69eaf1753eadbd74b4da203370ca8f2ee41d3961fb6cdebdba497679` | `sha256:2beb47ef4cf0882016b2655c3bcb74c96a9e676abfdb43924f694777434b9663` | `sha256:96f772a6b539890ce09fb4b1b2129a5e31451e1501eb2606d1f8b39a42c5ac9b` | The VS Code-native five-view workspace, canonical evidence navigation, governed agent execution journey, security boundaries, accessible desktop and compact states, deterministic Sharp/SVG renderer, and 23-PNG manifest are accepted. |
