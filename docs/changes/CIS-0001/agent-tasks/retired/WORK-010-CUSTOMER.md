---
title: "WORK-010-CUSTOMER Define textual screens, states, actions, and paths (customer)"
type: agent-task
status: Archived
task_status: Retired
task_id: WORK-010-CUSTOMER
task_type: core.design.wireframe
task_type_version: 1.1
parent_id: WORK-000
category: wireframe
complexity: medium
feature_spec_path: "docs/specs/features/vscode-delivery-workspace-feature.md"
feature_spec_sha256: "sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73"
requirement_ids: ["VSC-018"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-29D936157B","IMPACT-58444A32BD","IMPACT-0CD1F4C48D","IMPACT-C2D5D2E7D4","IMPACT-917D5EBEE4","IMPACT-12A19A137A","IMPACT-ABA78187E0","IMPACT-88C10295FC","IMPACT-4FFC76EB77","IMPACT-16036E2FE4","IMPACT-853D561CE3","IMPACT-09135EBC0E","IMPACT-3D815143B5","IMPACT-128EBFF949","IMPACT-9A81A2C697","IMPACT-64E368D4F0","IMPACT-31E8984C3F","IMPACT-A0D04362DF","IMPACT-50133A00ED","IMPACT-FE3C67EA75","IMPACT-6ECE7CCFA7","IMPACT-D370753277","IMPACT-1D7A625AEA"]
depends_on: ["WORK-000"]
approval_gate: none
frontend_type: customer
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-010-CUSTOMER: Define textual screens, states, actions, and paths (customer)

## Objective

Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid.
This task is bounded to the `customer` frontend surface.

## Required changes

- `VSC-018`: Installation shall verify compatible VS Code and CIS CLI versions and provide actionable recovery when either component is absent or incompatible.

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
- [ ] `VSC-018`: A packaged VSIX installs on the supported VS Code range, finds `cis` on `PATH` or an explicit executable path, reports the detected versions, and passes clean-profile installation and activation smoke tests.

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

## Retirement history

| Timestamp UTC | Actor | Source digest | Rationale |
|---|---|---|---|
| 2026-08-28T14:08:22.7616613+00:00 | `cis plan import-spec` | `sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73` | Task is no longer selected by the re-imported feature specification. |
