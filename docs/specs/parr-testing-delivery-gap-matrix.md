---
title: "PARR Testing and Delivery Gap Matrix"
type: gap-analysis
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on assurance-hardening milestone completion"
cis:
  stable_id: change-impact-studio:analysis:parr-testing-delivery-gap-matrix
---

# PARR testing and delivery gap matrix

## 1. Purpose

This matrix compares the implemented CIS golden path and the Friends Todo reference
delivery with the reusable delivery and testing expectations established by PARR. It
distinguishes policy that exists in CIS from behavior that is selected, executed, and
enforced in a target repository.

The matrix is not a claim that every PARR product test is universally applicable. PARR's
framework-specific choices remain examples. The portable expectations are distinct test
layers, representative integration boundaries, risk-based mutation and assurance,
changed-code coverage, regression evidence, and continuously enforced delivery gates.

## 2. Evidence boundary

The review used:

- the PARR testing standard, coverage-exclusion policy, architecture-test policy,
  mutation-score ratchet, browser-regression ADR, execution procedure, and testing
  skills;
- the CIS testing standard, task-type specifications, implementation skill packs,
  repository initialization, verification, assurance, and manual-test-case contracts;
- the complete Friends Todo BRD-to-delivery golden path and its recorded observations;
- the Friends Todo API, web, infrastructure, documentation, coverage, integration,
  Playwright, manual-test, verification, and assurance artifacts; and
- the latest recorded CIS-0010 deterministic verification: 146 API fast tests, 3
  SuperTokens Core integration tests, 105 web fast/component tests, and 2 Playwright
  browser scenarios.

## 3. Maturity scale

| Score | Meaning |
| ---: | --- |
| 0 | Absent; no reliable capability or evidence exists. |
| 1 | Documented or ad hoc; the expectation is visible but not represented as a repeatable contract. |
| 2 | Partially implemented; useful evidence exists but coverage, classification, or reproducibility is incomplete. |
| 3 | Implemented and repeatable when deliberately invoked, but not fully selected or continuously enforced. |
| 4 | Deterministically selected, validated, executed, and blocking for applicable changes. |
| 5 | Continuously enforced with ratchets, durable evidence, failure classification, and release integration. |

The target is not always five. Human approval and product authority remain intentionally
human-controlled, while mechanical test and delivery obligations should normally reach
four or five.

## 4. Delivery-process matrix

| ID | Capability | Current | Target | Gap and evidence | Priority | State |
| --- | --- | ---: | ---: | --- | --- | --- |
| GAP-PROC-001 | Canonical product authority | 5 | 4 | CIS provides governed BRD, technical intent, backlog, feature, plan, and final-acceptance lifecycles with content-drift detection. This is stronger than the minimum reusable PARR pattern. | - | Covered |
| GAP-PROC-002 | Multi-repository context and impact | 5 | 4 | Repository classification, workspace graph, accepted impacts, routing, and planned-versus-actual comparison are implemented across independent repositories. | - | Covered |
| GAP-PROC-003 | Task decomposition and provider policy | 4 | 4 | Nineteen core types, complexity decomposition, ordering, extension conflicts, selection, migration, and evidence carry-forward are implemented. | - | Covered |
| GAP-PROC-004 | Wireframe, visual design, and review barrier | 5 | 4 | Textual wireframes, reusable shell/components, deterministic Sharp/SVG rendering, PNG manifests, rejection history, and global design pause are implemented. | - | Covered |
| GAP-PROC-005 | Proportionate semantic approvals | 4 | 4 | Evidence-only reconciliation preserves valid authority and design reuse avoids duplicate approval. Final human acceptance remains correctly distinct from verification. | - | Covered |
| GAP-PROC-006 | Feature-specification authoring guidance | 2 | 4 | Lifecycle commands exist, but initialization does not seed a dedicated classification-aware feature-specification skill. This is Friends Todo observation `GP-099`. | P1 | Open |
| GAP-PROC-007 | Repository-specific workflow profiles | 1 | 4 | The Friends Todo authority repository received `dotnet build` and `dotnet test` despite governing TypeScript, Next.js, and Compose repositories. Workflow generation is not classification-bound. | P0 | Open |
| GAP-PROC-008 | Pull-request and release CI | 1 | 5 | Friends Todo has no repository-owned GitHub Actions workflows. Checks are executed manually or through an agent on the SSH build server and therefore do not continuously block regressions. | P0 | Open |
| GAP-PROC-009 | Verification failure classification | 2 | 4 | Tool usage records failures, but runner resource crashes can be counted like product failures. `GP-096` remains open for bounded rerun and infrastructure classification. | P1 | Open |
| GAP-PROC-010 | Release validation and artifact promotion | 2 | 5 | Build, runtime smoke, recovery, browser, and assurance evidence exist, but there is no ordered release gate, freshness re-check, artifact promotion dependency, or mutation threshold. | P1 | Open |

## 5. Test-suite matrix

| ID | Test or assurance layer | Current | Target | Gap and evidence | Priority | State |
| --- | --- | ---: | ---: | --- | --- | --- |
| GAP-TEST-001 | Canonical suite inventory and layer classification | 1 | 4 | No suite profile states which command, framework, layer, boundary, environment, and risk each test owns. Folder names are treated as truth even when behavior crosses layers. | P0 | Open |
| GAP-TEST-002 | Pure unit tests and unit coverage | 3 | 4 | Focused deterministic tests are extensive, but API `test/unit` also opens HTTP listeners and real SQLite files. The reported coverage cannot be identified as pure unit coverage. | P0 | Open |
| GAP-TEST-003 | Component and API integration | 2 | 4 | In-process HTTP behavior exists but is labelled unit. The explicit integration suite contains only three SuperTokens Core cases. API, serialization, security, and persistence integration need separate reporting. | P1 | Open |
| GAP-TEST-004 | Persistence and migration integration | 3 | 4 | Real SQLite migration, constraint, backup, and restore behavior is tested, but it is hidden in the unit suite and not a separately executable integration gate. | P1 | Open |
| GAP-TEST-005 | Business acceptance tests | 0 | 4 | No distinct business-readable suite proves owner/member, access approval, list/todo lifecycle, and session outcomes. Smoke and browser journeys do not replace this layer. | P1 | Open |
| GAP-TEST-006 | Architecture and policy tests | 2 | 4 | Public-route persistence isolation and route inventory checks exist. General source dependency direction, layer ownership, forbidden imports, and security architecture are not systematically enforced. | P1 | Open |
| GAP-TEST-007 | Frontend component and accessibility tests | 3 | 4 | React interaction and axe evidence is substantial, but it is not described by a canonical suite profile or tied to an always-on CI gate. | P1 | Open |
| GAP-TEST-008 | Browser regression | 2 | 4 | One Playwright file has two scenarios, including one long anonymous journey. Feature-scoped cloud, sharing, membership, concurrency, recovery, and session journeys are not independently isolated. | P1 | Open |
| GAP-TEST-009 | Browser failure artifacts | 1 | 4 | Playwright uses a line reporter with no trace, screenshot, video, or durable diagnostic artifact policy. | P1 | Open |
| GAP-TEST-010 | Mutation testing and score ratchet | 0 | 5 | No JavaScript mutation runner, configuration, score, survivor disposition, feature baseline, or enforcing release threshold exists. | P1 | Open |
| GAP-TEST-011 | Changed-code coverage | 2 | 4 | API and web have global V8 thresholds, but no changed-scope calculation, exclusion policy enforcement, or human-approved bounded exception. | P1 | Open |
| GAP-TEST-012 | Defect regression preservation | 3 | 4 | Several golden-path defects gained focused regression tests, but CIS does not inventory reproduced defects or prove that each applicable regression ran in the completion build. | P1 | Open |
| GAP-TEST-013 | Manual test catalogue coverage | 2 | 4 | CIS-0007 through CIS-0010 have 48 Markdown/CSV cases with zero pending mappings. CIS-0001 through CIS-0006 have no generated catalogue, and existing catalogues remain Draft. | P2 | Open |
| GAP-TEST-014 | Executed-test traceability | 2 | 4 | CIS validates stable `TC-*` source references, but a source match is not proof that the named test was discovered, executed, and passed in the recorded run. | P0 | Open |
| GAP-TEST-015 | Security assurance | 3 | 4 | Negative authorization tests, dependency audits, bounded secret scans, and non-disclosure browser assertions exist. SAST, dependency policy, and optional dynamic security scans are not represented as classified gates. | P2 | Open |
| GAP-TEST-016 | Provider and deployment-mode matrix | 2 | 4 | Anonymous mode has the broadest runtime journey. Cloud mode has adapter and ephemeral Core coverage but no managed-provider or real Google opt-in smoke profile. | P2 | Open |
| GAP-TEST-017 | Independent assurance separation | 2 | 4 | Mechanized checks provide separate failure surfaces, but the implementation and assurance reports may be produced by the same agent identity. Assignment and independence evidence are not enforced. | P2 | Open |
| GAP-TEST-018 | Cross-platform and remote execution | 3 | 4 | Windows/Linux, Docker, SSH-server, and Chromium evidence exists, but environment selection and results are manually assembled rather than emitted from a canonical workflow run. | P1 | Open |
| GAP-TEST-019 | Flaky/infrastructure failure handling | 1 | 4 | There is no retry classification, quarantine policy, flake history, or rule separating diagnostic reruns from canonical successful evidence. | P1 | Open |

## 6. Current measured Friends Todo baseline

| Surface | Files/scenarios | Latest result | Coverage or boundary |
| --- | ---: | --- | --- |
| API mixed fast suite | 23 test files; 146 tests | Passed | 90.17% lines; 86.24% statements; 97.32% functions; 82.55% branches |
| API explicit integration | 1 test file; 3 tests | Passed | Ephemeral SuperTokens Core through Testcontainers |
| Web mixed fast/component suite | 19 test files; 105 tests | Passed | 90.23% lines; 83.76% statements; 86.39% functions; 76.36% branches |
| Browser regression | 1 file; 2 scenarios | Passed | Chromium, anonymous composed journey, compact unknown-route behavior, and axe checks |
| Infrastructure | 4 executable validation scripts | Passed in recorded feature evidence | Compose topology, anonymous smoke, recovery drill, and operations report |
| Manual test catalogues | 4 changes; 48 cases | All mapped to source references | HLT-FR-006 through HLT-FR-009 only |

These percentages are repository fast-suite coverage. They are not pure unit coverage and
do not calculate the 95-percent new or materially changed production-code expectation
defined by the CIS and PARR standards.

## 7. Closure rules

A row moves to `Closed` only when:

1. the capability has a canonical CIS contract and classification/applicability rule;
2. repository initialization selects or omits it from deterministic evidence;
3. the applicable command or workflow can execute without conversational reconstruction;
4. machine-readable results distinguish passed, failed, skipped, unavailable, and not
   applicable outcomes;
5. `cis verify validate` rejects missing applicable evidence or stale traceability;
6. focused CIS tests and the Friends Todo golden path prove the behavior; and
7. documentation, command manuals, skills, instructions, and catalogs reconcile.

Human approval is required only for product scope, explicit exceptions, residual-risk
acceptance, and final outcome acceptance. Closing mechanical test gaps must not create a
new approval after every test layer or command.

## 8. Delivery plan

The ordered closure plan is defined in
[PARR testing and delivery assurance implementation plan](parr-testing-delivery-implementation-plan.md).

