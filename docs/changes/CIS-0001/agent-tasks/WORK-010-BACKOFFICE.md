---
title: "WORK-010-BACKOFFICE Define textual screens, states, actions, and paths (backoffice)"
type: agent-task
status: Draft
task_status: Approved
task_id: WORK-010-BACKOFFICE
task_type: core.design.wireframe
task_type_version: 1.1
parent_id: WORK-000
category: wireframe
complexity: medium
feature_spec_path: "docs/specs/features/vscode-delivery-workspace-feature.md"
feature_spec_sha256: "sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73"
requirement_ids: ["VSC-002","VSC-003","VSC-004","VSC-005","VSC-006","VSC-007","VSC-008","VSC-009","VSC-010","VSC-011","VSC-012","VSC-013","VSC-014","VSC-015","VSC-016","VSC-020","VSC-021","VSC-022","VSC-023"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-29D936157B","IMPACT-58444A32BD","IMPACT-0CD1F4C48D","IMPACT-C2D5D2E7D4","IMPACT-917D5EBEE4","IMPACT-12A19A137A","IMPACT-ABA78187E0","IMPACT-88C10295FC","IMPACT-4FFC76EB77","IMPACT-16036E2FE4","IMPACT-853D561CE3","IMPACT-09135EBC0E","IMPACT-3D815143B5","IMPACT-128EBFF949","IMPACT-9A81A2C697","IMPACT-64E368D4F0","IMPACT-31E8984C3F","IMPACT-A0D04362DF","IMPACT-50133A00ED","IMPACT-FE3C67EA75","IMPACT-6ECE7CCFA7","IMPACT-D370753277","IMPACT-1D7A625AEA"]
depends_on: ["WORK-000"]
approval_gate: none
frontend_type: backoffice
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-010-BACKOFFICE: Define textual screens, states, actions, and paths (backoffice)

## Objective

Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid.
This task is bounded to the `backoffice` frontend surface.

## Required changes

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
- `VSC-020`: The Runs view shall expose discovered agent providers and truthful provider diagnostics.
- `VSC-021`: An eligible planned task shall offer a governed Request agent work action.
- `VSC-022`: A foreground agent run shall remain observable and controllable from the workspace.
- `VSC-023`: Agent results shall remain evidence rather than lifecycle authority.

## Required outputs

- wireframes.md
- Validated screen/action/path coverage
- Exact digest bound to design review

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
- Accepted impact `IMPACT-C2D5D2E7D4`: `change-impact-studio::component::change-impact-studio` - change-impact-studio.
- Accepted impact `IMPACT-917D5EBEE4`: `change-impact-studio::source-file::vscode-extension/extension.js` - extension.js.
- Accepted impact `IMPACT-12A19A137A`: `change-impact-studio::source-file::vscode-extension/test/extension.test.js` - extension.test.js.
- Accepted impact `IMPACT-ABA78187E0`: `change-impact-studio::symbol::change-impact-studio/javascript/class/CisCli` - CisCli.
- Accepted impact `IMPACT-88C10295FC`: `change-impact-studio::symbol::change-impact-studio/javascript/class/CisTreeItem` - CisTreeItem.
- Accepted impact `IMPACT-4FFC76EB77`: `change-impact-studio::symbol::change-impact-studio/javascript/class/CisWorkspaceTree` - CisWorkspaceTree.
- Accepted impact `IMPACT-16036E2FE4`: `change-impact-studio::symbol::change-impact-studio/javascript/class/TreeItem` - TreeItem.
- Accepted impact `IMPACT-853D561CE3`: `change-impact-studio::symbol::change-impact-studio/javascript/function/activate` - activate.
- Accepted impact `IMPACT-09135EBC0E`: `change-impact-studio::symbol::change-impact-studio/javascript/function/deactivate` - deactivate.
- Accepted impact `IMPACT-3D815143B5`: `change-impact-studio::symbol::change-impact-studio/javascript/function/markdownFiles` - markdownFiles.
- Accepted impact `IMPACT-128EBFF949`: `change-impact-studio::symbol::change-impact-studio/javascript/function/resolveWithin` - resolveWithin.
- Accepted impact `IMPACT-9A81A2C697`: `change-impact-studio::workflow::.github%2Fworkflows%2Fci.yml#mutation` - mutation.
- Accepted impact `IMPACT-64E368D4F0`: `change-impact-studio::workflow::.github%2Fworkflows%2Fci.yml#release-smoke` - release-smoke.
- Accepted impact `IMPACT-31E8984C3F`: `change-impact-studio::workflow::.github%2Fworkflows%2Fci.yml#security` - security.
- Accepted impact `IMPACT-A0D04362DF`: `change-impact-studio::workflow::.github%2Fworkflows%2Fci.yml#verify` - verify.
- Accepted impact `IMPACT-50133A00ED`: `change-impact-studio::test::change-impact-studio/javascript-test/vscode-extension%2Ftest%2Fextension.test.js%23CisCli%20rejects%20queries%20without%20an%20open%20repository` - CisCli rejects queries without an open repository.
- Accepted impact `IMPACT-FE3C67EA75`: `change-impact-studio::test::change-impact-studio/javascript-test/vscode-extension%2Ftest%2Fextension.test.js%23markdown%20routing%20is%20deterministic%20and%20only%20returns%20markdown` - markdown routing is deterministic and only returns markdown.
- Accepted impact `IMPACT-6ECE7CCFA7`: `change-impact-studio::test::change-impact-studio/javascript-test/vscode-extension%2Ftest%2Fextension.test.js%23tree%20honors%20the%20initialized%20documentation%20root` - tree honors the initialized documentation root.
- Accepted impact `IMPACT-D370753277`: `change-impact-studio::test::change-impact-studio/javascript-test/vscode-extension%2Ftest%2Fextension.test.js%23tree%20rejects%20a%20documentation%20root%20that%20escapes%20the%20workspace` - tree rejects a documentation root that escapes the workspace.
- Accepted impact `IMPACT-1D7A625AEA`: `change-impact-studio::repository::change-impact-studio` - change-impact-studio.

## Dependencies and approval gates

- `WORK-000` - Feature delivery coordination and scope guard.

## Acceptance criteria

- [ ] Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid.
- [ ] `VSC-002`: The view distinguishes no folder, missing CLI, unsupported CLI, uninitialized repository, and initialized repository states and offers only the applicable primary action and documentation links.
- [ ] `VSC-003`: A native Quick Pick lists eligible folders, preserves the explicit selection per workspace, and never silently changes authority when folders are added or removed.
- [ ] `VSC-004`: Each summary item has a truthful state, opens its evidence or applicable command, and refreshes without requiring a VS Code reload.
- [ ] `VSC-005`: Changes are filterable without deep tree nesting; selecting a change opens its overview rather than executing a mutation.
- [ ] `VSC-006`: The recommendation is derived from CLI status, explains why it is available or blocked, and never represents a mechanical gate as a new human approval.
- [ ] `VSC-007`: Every summarized requirement, decision, plan, design, test case, and verification claim links to its canonical file; the UI does not maintain a competing editable copy.
- [ ] `VSC-008`: Search uses native input/Quick Pick behavior, shows truncation and freshness, opens file locations, and loads graph visualization only when explicitly requested.
- [ ] `VSC-009`: Task transitions invoke CIS commands, require rationale only where the CLI requires it, and refresh from canonical state after completion or failure.
- [ ] `VSC-010`: Reviewers can compare all declared screen states, enter a rationale, approve or reject through CIS, and cannot start downstream work while the CLI reports the design gate as paused.
- [ ] `VSC-011`: The view preserves first-attempt failures, failure classification, unavailable suites, repository revision, run ID, and artifact hashes; logs and retained artifacts open through bounded CIS retrieval.
- [ ] `VSC-012`: Inventories remain compact and searchable; conflicts, quarantines, stale profiles, and suggested fixes are visually distinct from healthy informational state.
- [ ] `VSC-013`: The command is launched as an argument array without a shell; success, warning, failure, timeout, cancellation, and invalid-evidence outcomes remain distinct and the original CLI exit code is retained.
- [ ] `VSC-014`: Refresh is debounced, never reads dependency or secret folders, marks stale data instead of presenting it as current, and does not continuously rerun expensive commands.
- [ ] `VSC-015`: Primary journeys complete without a mouse; focus returns to the initiating control after dialogs; status is never communicated by color alone; webview content uses VS Code theme tokens and accessible names.
- [ ] `VSC-016`: Every view has bounded recovery guidance; errors disclose no secret values or unrestricted process output and never replace a previous authoritative result with a false success.
- [ ] `VSC-020`: Codex and Claude availability, transport, version, authentication, supported run modes, permission capabilities, resume support, and failure reason come from `cis agent providers` or `cis agent provider diagnose`; the extension never reads or manages provider credentials.
- [ ] `VSC-021`: The controller selects only provider, declared run mode, permission ceiling, and target repository values supported by CIS; the extension invokes `cis agent prepare` and `cis agent run` as argument arrays, presents the exact task and ceiling before launch, and displays the precise CIS gate when execution is rejected.
- [ ] `VSC-022`: Normalized events show attempt, state, bounded progress, permission requests, isolation mode, provider session reference, cancellation, timeout, failure, and completion without rendering an unrestricted provider transcript; supported cancel, recover, and resume actions invoke CIS and every resume creates a visible new attempt.
- [ ] `VSC-023`: The run detail opens the bounded result, changed-file inventory, validations, artifacts, hashes, and imported task evidence; importing a result never approves scope, transitions a task, closes a change, or hides the required human review point.

## Targeted validation

- [ ] Primary and alternate flows are represented.
- [ ] Empty, loading, failure, denied, responsive, and lifecycle states are covered.
- [ ] Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| TODO | TODO | Not run | Record exact reproducible evidence. |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.
## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|
## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
