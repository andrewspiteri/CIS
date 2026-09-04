---
title: "CIS-0001 manual test cases"
type: manual-test-cases
status: Draft
change_id: CIS-0001
feature_spec_path: "docs/specs/features/vscode-delivery-workspace-feature.md"
feature_spec_sha256: "sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73"
test_case_count: 23
automated_test_case_count: 23
automation_pending_count: 0
csv_path: test-cases.csv
csv_sha256: "sha256:270bc825ef94a1ba59ae8279f47568923ac79504239f20ed28b0b6e14116fe81"
generation: deterministic
authority: derived
---

# Manual test cases

Generated from `docs/specs/features/vscode-delivery-workspace-feature.md`. Regenerate through feature planning after the source specification changes; do not record execution results in this derived catalogue.

The companion `test-cases.csv` uses one row per case and portable field names that can be mapped during import into TestRail or another test-management system.

## Coverage summary

| Test case | Title | Section | Priority | Type | Requirement | Frontend type | Automation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TC-VSC-001-001 | The extension shall remain a thin client over stable CIS CLI arguments, exit codes, JSON results, canonical Markdown,... | Contract | Low | Security | VSC-001 | not-applicable | Automated |
| TC-VSC-002-001 | An empty or uninitialized workspace shall show a concise native Welcome View. | Frontend / Backoffice | Low | User interface | VSC-002 | backoffice | Automated |
| TC-VSC-003-001 | The controller shall be able to select the CIS authority repository when more than one VS Code workspace folder is open. | Frontend / Backoffice | Low | User interface | VSC-003 | backoffice | Automated |
| TC-VSC-004-001 | The Workspace view shall summarize repository identity, documentation root, health, graph/index freshness, local AI a... | Frontend / Backoffice | Low | User interface | VSC-004 | backoffice | Automated |
| TC-VSC-005-001 | The Changes view shall present current and closed dossiers with phase, lifecycle, stale-state, and blocking-gate info... | Frontend / Backoffice | Low | User interface | VSC-005 | backoffice | Automated |
| TC-VSC-006-001 | A change overview shall present intent, progress, accepted scope, decisions, tasks, required human review points, and... | Frontend / Backoffice | Low | User interface | VSC-006 | backoffice | Automated |
| TC-VSC-007-001 | Canonical Markdown shall remain directly inspectable and editable through VS Code editors and built-in Markdown preview. | Frontend / Backoffice | Low | User interface | VSC-007 | backoffice | Automated |
| TC-VSC-008-001 | The controller shall be able to search bounded context and inspect source evidence and graph relationships. | Frontend / Backoffice | Medium | User interface | VSC-008 | backoffice | Automated |
| TC-VSC-009-001 | Plans and tasks shall show category, complexity, dependencies, acceptance criteria, status, owner, and blocking evide... | Frontend / Backoffice | Low | User interface | VSC-009 | backoffice | Automated |
| TC-VSC-010-001 | Design review shall show textual wireframes, rendered PNGs, provenance, manifest validation, and approve/reject actio... | Frontend / Backoffice | Low | User interface | VSC-010 | backoffice | Automated |
| TC-VSC-011-001 | Runs shall present workflow, test, coverage, mutation, security, diagnostics, and independent-assurance outcomes with... | Frontend / Backoffice | Medium | Security | VSC-011 | backoffice | Automated |
| TC-VSC-012-001 | Governance shall expose skills, instructions, standards, references, provider selections, and Repository Doctor findi... | Frontend / Backoffice | Low | User interface | VSC-012 | backoffice | Automated |
| TC-VSC-013-001 | Long-running or mutating commands shall have visible progress, cancellation where supported, and inspectable output. | Frontend / Backoffice | Low | User interface | VSC-013 | backoffice | Automated |
| TC-VSC-014-001 | The UI shall refresh deterministically when canonical files or relevant derived manifests change. | Frontend / Backoffice | Low | User interface | VSC-014 | backoffice | Automated |
| TC-VSC-015-001 | All views shall support keyboard operation, logical focus restoration, zoom, reduced motion, screen readers, and VS C... | Frontend / Backoffice | Low | Accessibility | VSC-015 | backoffice | Automated |
| TC-VSC-016-001 | Loading, empty, partial, unavailable, stale, warning, error, cancelled, and success states shall be explicitly designed. | Frontend / Backoffice | Low | User interface | VSC-016 | backoffice | Automated |
| TC-VSC-017-001 | The extension shall preserve CIS filesystem, process, credential, and evidence trust boundaries. | Security | Low | Security | VSC-017 | not-applicable | Automated |
| TC-VSC-018-001 | Installation shall verify compatible VS Code and CIS CLI versions and provide actionable recovery when either compone... | Delivery | Low | Functional | VSC-018 | not-applicable | Automated |
| TC-VSC-019-001 | User and contributor documentation shall describe installation, first use, navigation, review points, settings, troub... | Documentation | Low | Functional | VSC-019 | not-applicable | Automated |
| TC-VSC-020-001 | The Runs view shall expose discovered agent providers and truthful provider diagnostics. | Frontend / Backoffice | Low | Security | VSC-020 | backoffice | Automated |
| TC-VSC-021-001 | An eligible planned task shall offer a governed Request agent work action. | Frontend / Backoffice | Low | Security | VSC-021 | backoffice | Automated |
| TC-VSC-022-001 | A foreground agent run shall remain observable and controllable from the workspace. | Frontend / Backoffice | Low | Security | VSC-022 | backoffice | Automated |
| TC-VSC-023-001 | Agent results shall remain evidence rather than lifecycle authority. | Frontend / Backoffice | Low | User interface | VSC-023 | backoffice | Automated |

## Test case definitions

### TC-VSC-001-001: The extension shall remain a thin client over stable CIS CLI arguments, exit codes, JSON results, canonical Markdown,...

- Requirement: `VSC-001`
- Section: Contract
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/cli.integration.test.js:16`; `change-impact-studio::vscode-extension/test/extension.test.js:71`; `change-impact-studio::vscode-extension/test/extension.test.js:72`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-001.

#### Steps

1. Use the supported application or API client to reach the boundary governed by VSC-001.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The extension shall remain a thin client over stable CIS CLI arguments, exit codes, JSON results, canonical Markdown, and repository-local state.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

No graph, planning, approval, testing, security, or verification rule is reimplemented in the extension; every displayed authoritative state identifies its CLI or Markdown source.

### TC-VSC-002-001: An empty or uninitialized workspace shall show a concise native Welcome View.

- Requirement: `VSC-002`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:261`; `change-impact-studio::vscode-extension/test/extension.test.js:262`; `change-impact-studio::vscode-extension/test/extension.test.js:420`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-002.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-002.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: An empty or uninitialized workspace shall show a concise native Welcome View.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The view distinguishes no folder, missing CLI, unsupported CLI, uninitialized repository, and initialized repository states and offers only the applicable primary action and documentation links.

### TC-VSC-003-001: The controller shall be able to select the CIS authority repository when more than one VS Code workspace folder is open.

- Requirement: `VSC-003`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:138`; `change-impact-studio::vscode-extension/test/extension.test.js:139`; `change-impact-studio::vscode-extension/test/extension.test.js:420`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-003.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-003.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The controller shall be able to select the CIS authority repository when more than one VS Code workspace folder is open.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

A native Quick Pick lists eligible folders, preserves the explicit selection per workspace, and never silently changes authority when folders are added or removed.

### TC-VSC-004-001: The Workspace view shall summarize repository identity, documentation root, health, graph/index freshness, local AI a...

- Requirement: `VSC-004`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:278`; `change-impact-studio::vscode-extension/test/extension.test.js:279`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-004.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-004.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The Workspace view shall summarize repository identity, documentation root, health, graph/index freshness, local AI availability, active change, and blocking review state.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Each summary item has a truthful state, opens its evidence or applicable command, and refreshes without requiring a VS Code reload.

### TC-VSC-005-001: The Changes view shall present current and closed dossiers with phase, lifecycle, stale-state, and blocking-gate info...

- Requirement: `VSC-005`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:222`; `change-impact-studio::vscode-extension/test/extension.test.js:223`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-005.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-005.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The Changes view shall present current and closed dossiers with phase, lifecycle, stale-state, and blocking-gate information.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Changes are filterable without deep tree nesting; selecting a change opens its overview rather than executing a mutation.

### TC-VSC-006-001: A change overview shall present intent, progress, accepted scope, decisions, tasks, required human review points, and...

- Requirement: `VSC-006`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:208`; `change-impact-studio::vscode-extension/test/extension.test.js:209`; `change-impact-studio::vscode-extension/test/extension.test.js:278`; `change-impact-studio::vscode-extension/test/extension.test.js:279`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-006.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-006.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: A change overview shall present intent, progress, accepted scope, decisions, tasks, required human review points, and the single next recommended action.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The recommendation is derived from CLI status, explains why it is available or blocked, and never represents a mechanical gate as a new human approval.

### TC-VSC-007-001: Canonical Markdown shall remain directly inspectable and editable through VS Code editors and built-in Markdown preview.

- Requirement: `VSC-007`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:166`; `change-impact-studio::vscode-extension/test/extension.test.js:167`; `change-impact-studio::vscode-extension/test/extension.test.js:222`; `change-impact-studio::vscode-extension/test/extension.test.js:223`; `change-impact-studio::vscode-extension/test/extension.test.js:420`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-007.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-007.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Canonical Markdown shall remain directly inspectable and editable through VS Code editors and built-in Markdown preview.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Every summarized requirement, decision, plan, design, test case, and verification claim links to its canonical file; the UI does not maintain a competing editable copy.

### TC-VSC-008-001: The controller shall be able to search bounded context and inspect source evidence and graph relationships.

- Requirement: `VSC-008`
- Section: Frontend / Backoffice
- Priority: Medium
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:304`; `change-impact-studio::vscode-extension/test/extension.test.js:305`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-008.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-008.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The controller shall be able to search bounded context and inspect source evidence and graph relationships.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Search uses native input/Quick Pick behavior, shows truncation and freshness, opens file locations, and loads graph visualization only when explicitly requested.

### TC-VSC-009-001: Plans and tasks shall show category, complexity, dependencies, acceptance criteria, status, owner, and blocking evide...

- Requirement: `VSC-009`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:208`; `change-impact-studio::vscode-extension/test/extension.test.js:209`; `change-impact-studio::vscode-extension/test/extension.test.js:420`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-009.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-009.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Plans and tasks shall show category, complexity, dependencies, acceptance criteria, status, owner, and blocking evidence.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Task transitions invoke CIS commands, require rationale only where the CLI requires it, and refresh from canonical state after completion or failure.

### TC-VSC-010-001: Design review shall show textual wireframes, rendered PNGs, provenance, manifest validation, and approve/reject actio...

- Requirement: `VSC-010`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:197`; `change-impact-studio::vscode-extension/test/extension.test.js:198`; `change-impact-studio::vscode-extension/test/extension.test.js:312`; `change-impact-studio::vscode-extension/test/extension.test.js:313`; `change-impact-studio::vscode-extension/test/extension.test.js:421`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-010.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-010.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Design review shall show textual wireframes, rendered PNGs, provenance, manifest validation, and approve/reject actions at the global design barrier.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Reviewers can compare all declared screen states, enter a rationale, approve or reject through CIS, and cannot start downstream work while the CLI reports the design gate as paused.

### TC-VSC-011-001: Runs shall present workflow, test, coverage, mutation, security, diagnostics, and independent-assurance outcomes with...

- Requirement: `VSC-011`
- Section: Frontend / Backoffice
- Priority: Medium
- Type: Security
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:244`; `change-impact-studio::vscode-extension/test/extension.test.js:245`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-011.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-011.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Runs shall present workflow, test, coverage, mutation, security, diagnostics, and independent-assurance outcomes without conflating them.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The view preserves first-attempt failures, failure classification, unavailable suites, repository revision, run ID, and artifact hashes; logs and retained artifacts open through bounded CIS retrieval.

### TC-VSC-012-001: Governance shall expose skills, instructions, standards, references, provider selections, and Repository Doctor findi...

- Requirement: `VSC-012`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:327`; `change-impact-studio::vscode-extension/test/extension.test.js:328`; `change-impact-studio::vscode-extension/test/extension.test.js:421`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-012.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-012.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Governance shall expose skills, instructions, standards, references, provider selections, and Repository Doctor findings.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Inventories remain compact and searchable; conflicts, quarantines, stale profiles, and suggested fixes are visually distinct from healthy informational state.

### TC-VSC-013-001: Long-running or mutating commands shall have visible progress, cancellation where supported, and inspectable output.

- Requirement: `VSC-013`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:108`; `change-impact-studio::vscode-extension/test/extension.test.js:109`; `change-impact-studio::vscode-extension/test/extension.test.js:421`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-013.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-013.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Long-running or mutating commands shall have visible progress, cancellation where supported, and inspectable output.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The command is launched as an argument array without a shell; success, warning, failure, timeout, cancellation, and invalid-evidence outcomes remain distinct and the original CLI exit code is retained.

### TC-VSC-014-001: The UI shall refresh deterministically when canonical files or relevant derived manifests change.

- Requirement: `VSC-014`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:410`; `change-impact-studio::vscode-extension/test/extension.test.js:411`; `change-impact-studio::vscode-extension/test/extension.test.js:421`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-014.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-014.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The UI shall refresh deterministically when canonical files or relevant derived manifests change.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Refresh is debounced, never reads dependency or secret folders, marks stale data instead of presenting it as current, and does not continuously rerun expensive commands.

### TC-VSC-015-001: All views shall support keyboard operation, logical focus restoration, zoom, reduced motion, screen readers, and VS C...

- Requirement: `VSC-015`
- Section: Frontend / Backoffice
- Priority: Low
- Type: Accessibility
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:197`; `change-impact-studio::vscode-extension/test/extension.test.js:198`; `change-impact-studio::vscode-extension/test/extension.test.js:312`; `change-impact-studio::vscode-extension/test/extension.test.js:313`; `change-impact-studio::vscode-extension/test/extension.test.js:422`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-015.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-015.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: All views shall support keyboard operation, logical focus restoration, zoom, reduced motion, screen readers, and VS Code light, dark, and high-contrast themes.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Primary journeys complete without a mouse; focus returns to the initiating control after dialogs; status is never communicated by color alone; webview content uses VS Code theme tokens and accessible names.

### TC-VSC-016-001: Loading, empty, partial, unavailable, stale, warning, error, cancelled, and success states shall be explicitly designed.

- Requirement: `VSC-016`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:261`; `change-impact-studio::vscode-extension/test/extension.test.js:262`; `change-impact-studio::vscode-extension/test/extension.test.js:278`; `change-impact-studio::vscode-extension/test/extension.test.js:279`; `change-impact-studio::vscode-extension/test/extension.test.js:346`; `change-impact-studio::vscode-extension/test/extension.test.js:347`; `change-impact-studio::vscode-extension/test/extension.test.js:422`; `change-impact-studio::vscode-extension/test/extension.test.js:424`; `change-impact-studio::vscode-extension/test/extension.test.js:71`; `change-impact-studio::vscode-extension/test/extension.test.js:72`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-016.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-016.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Loading, empty, partial, unavailable, stale, warning, error, cancelled, and success states shall be explicitly designed.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Every view has bounded recovery guidance; errors disclose no secret values or unrestricted process output and never replace a previous authoritative result with a false success.

### TC-VSC-017-001: The extension shall preserve CIS filesystem, process, credential, and evidence trust boundaries.

- Requirement: `VSC-017`
- Section: Security
- Priority: Low
- Type: Security
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:108`; `change-impact-studio::vscode-extension/test/extension.test.js:109`; `change-impact-studio::vscode-extension/test/extension.test.js:166`; `change-impact-studio::vscode-extension/test/extension.test.js:167`; `change-impact-studio::vscode-extension/test/extension.test.js:197`; `change-impact-studio::vscode-extension/test/extension.test.js:198`; `change-impact-studio::vscode-extension/test/extension.test.js:71`; `change-impact-studio::vscode-extension/test/extension.test.js:72`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-017.

#### Steps

1. Prepare the supported product or operational boundary governed by VSC-017.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The extension shall preserve CIS filesystem, process, credential, and evidence trust boundaries.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

It reads only selected workspace files and bounded CLI output, never reads credentials or `.env` content, applies workspace containment to opened paths, and requires workspace trust for process execution.

### TC-VSC-018-001: Installation shall verify compatible VS Code and CIS CLI versions and provide actionable recovery when either compone...

- Requirement: `VSC-018`
- Section: Delivery
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/cli.integration.test.js:16`; `change-impact-studio::vscode-extension/test/extension.test.js:261`; `change-impact-studio::vscode-extension/test/extension.test.js:262`; `change-impact-studio::vscode-extension/test/extension.test.js:422`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-018.

#### Steps

1. Prepare the supported product or operational boundary governed by VSC-018.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Installation shall verify compatible VS Code and CIS CLI versions and provide actionable recovery when either component is absent or incompatible.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

A packaged VSIX installs on the supported VS Code range, finds `cis` on `PATH` or an explicit executable path, reports the detected versions, and passes clean-profile installation and activation smoke tests.

### TC-VSC-019-001: User and contributor documentation shall describe installation, first use, navigation, review points, settings, troub...

- Requirement: `VSC-019`
- Section: Documentation
- Priority: Low
- Type: Functional
- Frontend type: `not-applicable`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:363`; `change-impact-studio::vscode-extension/test/extension.test.js:364`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-019.

#### Steps

1. Prepare the supported product or operational boundary governed by VSC-019.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: User and contributor documentation shall describe installation, first use, navigation, review points, settings, troubleshooting, and the thin-client boundary.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The VSIX README and CIS manual agree with implemented commands and screenshots and contain no unpublished or unsupported workflow claims.

### TC-VSC-020-001: The Runs view shall expose discovered agent providers and truthful provider diagnostics.

- Requirement: `VSC-020`
- Section: Frontend / Backoffice
- Priority: Low
- Type: Security
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/cli.integration.test.js:16`; `change-impact-studio::vscode-extension/test/extension.test.js:244`; `change-impact-studio::vscode-extension/test/extension.test.js:245`; `change-impact-studio::vscode-extension/test/extension.test.js:371`; `change-impact-studio::vscode-extension/test/extension.test.js:372`; `change-impact-studio::vscode-extension/test/extension.test.js:422`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-020.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-020.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: The Runs view shall expose discovered agent providers and truthful provider diagnostics.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Codex and Claude availability, transport, version, authentication, supported run modes, permission capabilities, resume support, and failure reason come from `cis agent providers` or `cis agent provider diagnose`; the extension never reads or manages provider credentials.

### TC-VSC-021-001: An eligible planned task shall offer a governed Request agent work action.

- Requirement: `VSC-021`
- Section: Frontend / Backoffice
- Priority: Low
- Type: Security
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:371`; `change-impact-studio::vscode-extension/test/extension.test.js:372`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-021.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-021.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: An eligible planned task shall offer a governed Request agent work action.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The controller selects only provider, declared run mode, permission ceiling, and target repository values supported by CIS; the extension invokes `cis agent prepare` and `cis agent run` as argument arrays, presents the exact task and ceiling before launch, and displays the precise CIS gate when execution is rejected.

### TC-VSC-022-001: A foreground agent run shall remain observable and controllable from the workspace.

- Requirement: `VSC-022`
- Section: Frontend / Backoffice
- Priority: Low
- Type: Security
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:108`; `change-impact-studio::vscode-extension/test/extension.test.js:109`; `change-impact-studio::vscode-extension/test/extension.test.js:371`; `change-impact-studio::vscode-extension/test/extension.test.js:372`; `change-impact-studio::vscode-extension/test/extension.test.js:423`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-022.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-022.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: A foreground agent run shall remain observable and controllable from the workspace.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

Normalized events show attempt, state, bounded progress, permission requests, isolation mode, provider session reference, cancellation, timeout, failure, and completion without rendering an unrestricted provider transcript; supported cancel, recover, and resume actions invoke CIS and every resume creates a visible new attempt.

### TC-VSC-023-001: Agent results shall remain evidence rather than lifecycle authority.

- Requirement: `VSC-023`
- Section: Frontend / Backoffice
- Priority: Low
- Type: User interface
- Frontend type: `backoffice`
- Automation status: Automated
- Automated test references: `change-impact-studio::vscode-extension/test/extension.test.js:166`; `change-impact-studio::vscode-extension/test/extension.test.js:167`; `change-impact-studio::vscode-extension/test/extension.test.js:244`; `change-impact-studio::vscode-extension/test/extension.test.js:245`; `change-impact-studio::vscode-extension/test/extension.test.js:371`; `change-impact-studio::vscode-extension/test/extension.test.js:372`; `change-impact-studio::vscode-extension/test/extension.test.js:423`; `change-impact-studio::vscode-extension/test/extension.test.js:424`

#### Preconditions

The feature build matching sha256:ce2683513b826563779bf48fb60afd9e9952f42916bb766fb5d6a0e48e51bf73 is deployed in a manual-test environment. The tester has the actor, data, configuration, and permissions needed for VSC-023.

#### Steps

1. Open the approved backoffice entry point for requirement VSC-023.
1. Establish the normal actor, data, and configuration preconditions without bypassing authorization or validation.
1. Perform the required behavior: Agent results shall remain evidence rather than lifecycle authority.
1. Observe the resulting user-visible, API, persistence, and operational state that applies.

#### Expected result

The run detail opens the bounded result, changed-file inventory, validations, artifacts, hashes, and imported task evidence; importing a result never approves scope, transitions a task, closes a change, or hides the required human review point.
