---
title: "WORK-040 Implement security, permissions, and exposure boundaries"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-040
task_type: core.security.permissions
task_type_version: 1.0
parent_id: WORK-000
category: security
complexity: medium
feature_spec_path: "docs/specs/features/vscode-delivery-workspace-feature.md"
feature_spec_sha256: "sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73"
requirement_ids: ["VSC-001","VSC-003","VSC-004","VSC-013","VSC-014","VSC-016","VSC-017","VSC-020","VSC-021","VSC-022","VSC-023"]
impact_ids: ["IMPACT-196BBD00E2","IMPACT-29D936157B","IMPACT-58444A32BD","IMPACT-0CD1F4C48D","IMPACT-C2D5D2E7D4","IMPACT-917D5EBEE4","IMPACT-12A19A137A","IMPACT-ABA78187E0","IMPACT-88C10295FC","IMPACT-4FFC76EB77","IMPACT-16036E2FE4","IMPACT-853D561CE3","IMPACT-09135EBC0E","IMPACT-3D815143B5","IMPACT-128EBFF949","IMPACT-9A81A2C697","IMPACT-64E368D4F0","IMPACT-31E8984C3F","IMPACT-A0D04362DF","IMPACT-50133A00ED","IMPACT-FE3C67EA75","IMPACT-6ECE7CCFA7","IMPACT-D370753277","IMPACT-1D7A625AEA"]
depends_on: ["WORK-020-BACKOFFICE","WORK-030"]
approval_gate: none
frontend_type: not-applicable
targets: ["change-impact-studio"]
decision_ids: []
authority: human-approved-plan
---

# WORK-040: Implement security, permissions, and exposure boundaries

## Objective

Positive and negative access paths enforce the approved security and visibility model.

## Required changes

- No credentials, stored provider prompts, `.env` values, unrestricted source bundles,
- or raw tracker bodies are read by the extension. Provider-native authentication stays
- outside the extension.
- Webviews use a restrictive content security policy, nonces for scripts, local resource
- roots, encoded content, and message validation.
- CLI execution is disabled in untrusted workspaces. Read-only Markdown navigation may
- remain available when VS Code permits it.
- Diagnostics shown in notifications are concise and redacted; full bounded evidence is
- opened through the CIS artifact or diagnostics commands.
- `VSC-001`: The extension shall remain a thin client over stable CIS CLI arguments, exit codes, JSON results, canonical Markdown, and repository-local state.
- `VSC-003`: The controller shall be able to select the CIS authority repository when more than one VS Code workspace folder is open.
- `VSC-004`: The Workspace view shall summarize repository identity, documentation root, health, graph/index freshness, local AI availability, active change, and blocking review state.
- `VSC-013`: Long-running or mutating commands shall have visible progress, cancellation where supported, and inspectable output.
- `VSC-014`: The UI shall refresh deterministically when canonical files or relevant derived manifests change.
- `VSC-016`: Loading, empty, partial, unavailable, stale, warning, error, cancelled, and success states shall be explicitly designed.
- `VSC-017`: The extension shall preserve CIS filesystem, process, credential, and evidence trust boundaries.
- `VSC-020`: The Runs view shall expose discovered agent providers and truthful provider diagnostics.
- `VSC-021`: An eligible planned task shall offer a governed Request agent work action.
- `VSC-022`: A foreground agent run shall remain observable and controllable from the workspace.
- `VSC-023`: Agent results shall remain evidence rather than lifecycle authority.

## Required outputs

- Completed security artifacts
- Reproducible validation evidence

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

- `WORK-020-BACKOFFICE` - Render and approve the visual design pack (backoffice).
- `WORK-030` - Align specifications, contracts, and references.

## Acceptance criteria

- [x] Positive and negative access paths enforce the approved security and visibility model.

## Targeted validation

- [x] Positive and prohibited access paths are tested.
- [x] Sensitive data and secrets are not exposed.
- [x] Run policy, authorization, data-exposure, secret, abuse-case, and security regression checks.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Extension syntax and security unit suite | `node --check extension.js`; individual `node --check lib/*.js`; `node --test test/*.test.js` from `vscode-extension/` | Passed | 10 tests cover workspace trust, multi-root authority, argument construction, path containment, ignored sensitive paths, redaction, bounded diagnostics, CSP/nonces, message allowlists, and debounced refresh. |
| Agent lifecycle concurrency suite | `dotnet test tests/Cis.Modules.Agent.Tests/Cis.Modules.Agent.Tests.csproj --no-restore` | Passed | 23 tests; includes concurrent cancellation, atomic manifest persistence, serialized event logging, terminal cancellation precedence, permission ceilings, redaction, and process containment. |
| Package manifest parse | `node -e "JSON.parse(require('node:fs').readFileSync('vscode-extension/package.json','utf8'))"` | Passed | Five Views, restricted untrusted-workspace capability, commands, settings, and menus are valid JSON. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1224; failed=97; possibleTokenSavings=524157; ledgerDigest=sha256:d80acf5cbe623738a8df3c24cc614be8b3b5af8c7d2f6abc18dfc5766c0e2cbc |
## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.
## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|
## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|
