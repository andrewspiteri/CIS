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
feature_spec_path: "docs/specs/features/vscode-delivery-workspace-feature.md"
feature_spec_sha256: "sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73"
requirement_ids: ["VSC-001","VSC-002","VSC-003","VSC-004","VSC-005","VSC-006","VSC-007","VSC-008","VSC-009","VSC-010","VSC-011","VSC-012","VSC-013","VSC-014","VSC-015","VSC-016","VSC-017","VSC-018","VSC-019","VSC-020","VSC-021","VSC-022","VSC-023"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-29D936157B","IMPACT-58444A32BD","IMPACT-0CD1F4C48D"]
depends_on: ["WORK-020-BACKOFFICE"]
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

- `VSC-001`: The extension shall remain a thin client over stable CIS CLI arguments, exit codes, JSON results, canonical Markdown, and repository-local state.
- `VSC-002`: An empty or uninitialized workspace shall show a concise native Welcome View.
- `VSC-003`: The controller shall be able to select the CIS authority repository when more than one VS Code workspace folder is open.
- `VSC-004`: The Workspace view shall summarize repository identity, documentation root, health, graph/index freshness, local AI availability, active change, and blocking review state.
- `VSC-005`: The Changes view shall present current and closed dossiers with phase, lifecycle, stale-state, and blocking-gate information.
- `VSC-006`: A change overview shall present intent, progress, accepted scope, decisions, tasks, required human review points, and the single next recommended action.
- `VSC-007`: Canonical Markdown shall remain directly inspectable and editable through VS Code editors and built-in Markdown preview.
- `VSC-008`: The controller shall be able to search bounded context and inspect source evidence and graph relationships.
- `VSC-009`: Plans and tasks shall show category, complexity, dependencies, acceptance criteria, status, owner, and blocking evidence.
- `VSC-010`: Design review shall show textual wireframes, rendered PNGs, provenance, manifest validation, and approve/reject actions at the global design barrier.
- `VSC-011`: Runs shall present workflow, test, coverage, mutation, security, diagnostics, and independent-assurance outcomes without conflating them.
- `VSC-012`: Governance shall expose skills, instructions, standards, references, provider selections, and Repository Doctor findings.
- `VSC-013`: Long-running or mutating commands shall have visible progress, cancellation where supported, and inspectable output.
- `VSC-014`: The UI shall refresh deterministically when canonical files or relevant derived manifests change.
- `VSC-015`: All views shall support keyboard operation, logical focus restoration, zoom, reduced motion, screen readers, and VS Code light, dark, and high-contrast themes.
- `VSC-016`: Loading, empty, partial, unavailable, stale, warning, error, cancelled, and success states shall be explicitly designed.
- `VSC-017`: The extension shall preserve CIS filesystem, process, credential, and evidence trust boundaries.
- `VSC-018`: Installation shall verify compatible VS Code and CIS CLI versions and provide actionable recovery when either component is absent or incompatible.
- `VSC-019`: User and contributor documentation shall describe installation, first use, navigation, review points, settings, troubleshooting, and the thin-client boundary.
- `VSC-020`: The Runs view shall expose discovered agent providers and truthful provider diagnostics.
- `VSC-021`: An eligible planned task shall offer a governed Request agent work action.
- `VSC-022`: A foreground agent run shall remain observable and controllable from the workspace.
- `VSC-023`: Agent results shall remain evidence rather than lifecycle authority.

## Required outputs

- Updated canonical specifications, contracts, decisions, and references

## Constraints and exclusions

- Reimplementing the CIS engine in TypeScript or a webview.
- A general project-management board, source-control client, chat interface, agent
- conversation transcript, provider account manager, credential broker, background agent
- scheduler, or detached agent daemon.
- Editing arbitrary `.cis/local/` JSON or SQLite-derived state.
- Automatic approval, rejection, impact disposition, exception creation, or final
- acceptance.
- A browser-only extension in this slice; the CLI requires a local executable and local
- repository filesystem.
- Marketplace publication or making the GitHub repository public before the private
- adoption review is complete.

## Context and evidence

- Canonical feature specification: `docs/specs/features/vscode-delivery-workspace-feature.md` (`sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73`).
- Accepted impact `IMPACT-196BBD00E2`: `change-impact-studio::document::change-impact-studio:feature:agent-execution-coordination` - Provider-Neutral Agent Execution Coordination.
- Accepted impact `IMPACT-29D936157B`: `change-impact-studio::document::change-impact-studio:feature:vscode-delivery-workspace` - Visual Studio Code Delivery Workspace.
- Accepted impact `IMPACT-58444A32BD`: `change-impact-studio::document::change-impact-studio:reference:repository-profile` - change-impact-studio Repository Profile.
- Accepted impact `IMPACT-0CD1F4C48D`: `change-impact-studio::document::change-impact-studio:spec:vscode-client` - Visual Studio Code Thin Client.

## Dependencies and approval gates

- `WORK-020-BACKOFFICE` - Render and approve the visual design pack (backoffice).

## Acceptance criteria

- [x] All affected canonical documentation agrees with approved behavior and exclusions.

## Targeted validation

- [x] Strict documentation and front-matter validation pass.
- [x] Affected contract/reference drift checks pass.
- [x] Run strict documentation and applicable contract/reference drift validation.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Canonical documentation | `docs/specs/vscode-client-spec.md`, `docs/manual/cis_vscode_extension.md`, `vscode-extension/README.md`, `docs/catalog.yml` | Passed | Consumed commands, authority selection, five-view navigation, agent boundary, security, installation, accessibility, and troubleshooting agree with the approved feature and design. |
| Documentation catalogue | `cis docs validate --repo . --strict --format agent` | Passed | 428 catalogue entries, 428 Markdown documents, zero warnings and zero errors. |
| Reference governance | `cis references validate --repo . --format agent` | Passed | Zero warnings and zero errors. |
| Plan/source agreement | `cis plan validate CIS-0001 --repo . --format agent` | Passed | 24 accepted impacts covered, zero open decisions, approved design gate preserved. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1216; failed=97; possibleTokenSavings=524157; ledgerDigest=sha256:d6c9b0534a7c6921fe12d87c699478aa40a6b50a3d3f692221998e01772d5798 |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.
## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|
## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
