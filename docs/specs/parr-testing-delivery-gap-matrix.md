---
title: "PARR Testing and Delivery Gap Matrix"
type: gap-analysis
status: Active
version: "1.0"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
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
- the reconciled `gap-closure-release-20260827` multi-repository run, the clean
  `/data` CIS 0.3.0 release build, and repository-owned GitHub Actions evidence.

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
| GAP-PROC-006 | Feature-specification authoring guidance | 4 | 4 | Initialization seeds the classification-aware `GP-099` feature-specification governance skill and linked agent instructions. | P1 | Closed |
| GAP-PROC-007 | Repository-specific workflow profiles | 4 | 4 | Classification-selected profiles detect .NET, Node/Vitest/Cucumber/Playwright, Next.js, Compose, Terraform, and documentation repositories while preserving reviewed commands on reinitialization. | P0 | Closed |
| GAP-PROC-008 | Pull-request and release CI | 5 | 5 | API, web, and infrastructure use repository-specific `/data` runners; docs uses GitHub-hosted Ubuntu. Strict required checks are enabled after green baselines. Public docs installation awaits the separately authorized CIS repository visibility change. | P0 | Implemented; external visibility pending |
| GAP-PROC-009 | Verification failure classification | 4 | 4 | `GP-096` is implemented with immutable attempt numbers and product, infrastructure, prerequisite, timeout, cancellation, and unknown classifications. A live missing-Playwright failure exposed and proved the prerequisite path. | P1 | Closed |
| GAP-PROC-010 | Release validation and artifact promotion | 5 | 5 | Ordered release workflows gate freshness, coverage, mutation, browser, security, integration, and operations evidence before bounded artifact upload. CIS 0.3.0 is packaged as NuGet tool, source archive, VSIX, and checksums. | P1 | Closed |

## 5. Test-suite matrix

| ID | Test or assurance layer | Current | Target | Gap and evidence | Priority | State |
| --- | --- | ---: | ---: | --- | --- | --- |
| GAP-TEST-001 | Canonical suite inventory and layer classification | 4 | 4 | Canonical suite profiles bind stable IDs, layers, frameworks, commands, working directories, result formats, prerequisites, applicability, CI tiers, and artifacts. | P0 | Closed |
| GAP-TEST-002 | Pure unit tests and unit coverage | 4 | 4 | API pure-unit execution excludes listeners, SQLite test fixtures, containers, browsers, and operational scripts and reports a separate 78.58% line-coverage baseline. | P0 | Closed |
| GAP-TEST-003 | Component and API integration | 4 | 4 | In-process HTTP component behavior and provider integration are independently executable and reconciled: 51 component cases and 4 ephemeral-Core cases. | P1 | Closed |
| GAP-TEST-004 | Persistence and migration integration | 4 | 4 | The SQLite suite independently proves 35 migration, constraint, transaction, idempotency, backup, restore, and recovery cases. | P1 | Closed |
| GAP-TEST-005 | Business acceptance tests | 4 | 4 | Cucumber 13.2.1 provides six business-readable scenarios covering the approved identity, lifecycle, sharing, membership, authorization, and termination outcomes. | P1 | Closed |
| GAP-TEST-006 | Architecture and policy tests | 4 | 4 | Compiler-backed API and web checks enforce dependency direction, public-cache isolation, frontend classification, and security boundaries. | P1 | Closed |
| GAP-TEST-007 | Frontend component and accessibility tests | 4 | 4 | Fifty-eight React component/accessibility cases are separately profiled, reported, and continuously gated. | P1 | Closed |
| GAP-TEST-008 | Browser regression | 4 | 4 | Six zero-retry Playwright scenarios cover authentication/session, list/todo, sharing/membership, unavailable/recovery, termination, and one thin golden journey. | P1 | Closed |
| GAP-TEST-009 | Browser failure artifacts | 4 | 4 | Playwright retains traces and screenshots on failure, optional failure video, error context, sanitized console, request-failure, authentication-response and page-error evidence, the managed harness log, and Compose logs/state captured before cleanup. | P1 | Closed |
| GAP-TEST-010 | Mutation testing and score ratchet | 5 | 5 | Stryker 10.0.0 with the Vitest runner enforces `high 85`, `low 80`, `break 80`; the selected 307-mutant scope scores 83.33% and a durable 25-finding no-new-survivor baseline. | P1 | Closed |
| GAP-TEST-011 | Changed-code coverage | 4 | 4 | API and web enforce 95% line coverage for changed production code, global baseline non-regression, and owner/reason metadata for narrow exclusions. | P1 | Closed |
| GAP-TEST-012 | Defect regression preservation | 4 | 4 | Stable defect and feature identities are reconciled from the exact canonical run; source-only references cannot satisfy verification. | P1 | Closed |
| GAP-TEST-013 | Manual test catalogue coverage | 4 | 4 | All ten changes have Markdown and CSV catalogues: 112 cases, 112 automated mappings, current source digests and CSV hashes, and passed exact-run trace evidence. | P2 | Closed |
| GAP-TEST-014 | Executed-test traceability | 4 | 4 | `cis test trace` requires each exact `TC-*` identity in a passed reconciled execution; all 112 Friends Todo cases pass against `gap-closure-release-20260827`. | P0 | Closed |
| GAP-TEST-015 | Security assurance | 4 | 4 | API/web CodeQL, dependency audits, bounded secret scanning, Terraform configuration scanning, negative-principal behavior, and non-disclosure checks are classified gates. | P2 | Closed |
| GAP-TEST-016 | Provider and deployment-mode matrix | 4 | 4 | Anonymous composed behavior, ephemeral SuperTokens Core, cloud adapter behavior, and an opt-in managed Google smoke are explicit. Missing credentials produce `unavailable`, never a pass. | P2 | Closed with optional provider unavailable |
| GAP-TEST-017 | Independent assurance separation | 4 | 4 | Reconciled manifests bind implementer, assurer, technique, profile digest, revision, run ID, and artifact hashes; repeated implementer evidence is rejected without a distinct reviewer or independent technique. | P2 | Closed |
| GAP-TEST-018 | Cross-platform and remote execution | 4 | 4 | CIS passes Windows and Ubuntu CI plus a clean Linux 0.3.0 release build on `/data`; Friends Todo emits canonical manifests from repository-specific `/data` runners. | P1 | Closed |
| GAP-TEST-019 | Flaky/infrastructure failure handling | 4 | 4 | Canonical retries remain zero, attempts are immutable, diagnostic reruns are distinct, product, runner, prerequisite, timeout, cancellation, and unknown failures are classified, and first-failure evidence is retained before managed cleanup. | P1 | Closed |
| GAP-TEST-020 | Test-time runtime log instrumentation | 4 | 4 | Workflow output streams to bounded attempt logs while the process runs; common credentials are redacted; partial timeout output survives; Testcontainers, browser, and Compose harnesses emit bounded run- and attempt-specific suite diagnostics; diagnostic reruns cannot erase earlier attempts; reconciliation hashes and correlates both evidence classes to the run, attempt, suite, component, revision, and test identities. | P1 | Closed |

## 6. Current measured Friends Todo baseline

| Surface | Files/scenarios | Latest result | Coverage or boundary |
| --- | ---: | --- | --- |
| API pure unit | 99 cases | Passed | 78.58% lines; no listeners, SQLite fixtures, containers, browsers, or operations |
| API component | 51 cases | Passed | In-process HTTP, middleware, serialization, status, headers, and Problem Details |
| API SQLite integration | 35 cases | Passed | Migrations, constraints, transactions, idempotency, backup, restore, and recovery |
| API provider integration | 4 ephemeral-Core cases; 1 managed smoke | Core passed; managed smoke unavailable | Testcontainers Core is deterministic; managed Google remains credential-dependent |
| API business / architecture / security | 6 / 5 / 21 cases | Passed | Cucumber outcomes, compiler boundaries, and negative security behavior |
| API aggregate coverage / mutation | 194 cases / 307 mutants | Passed / passed with findings | 89.65% aggregate lines; 95% changed-code gate; 83.33% mutation score |
| Web unit / component / architecture / security | 42 / 58 / 1 / 27 cases | Passed | Utilities, React/accessibility, compiler boundaries, and client security |
| Web browser | 6 scenarios | Passed | Feature-scoped Chromium scenarios plus one thin golden journey; zero retries |
| Infrastructure | 4 suites | Passed | Topology, anonymous runtime smoke, recovery drill, and operations evidence |
| Documentation and manual catalogues | 305 Markdown documents; 112 cases | Passed | Ten Markdown/CSV catalogues with 112 exact passed automation mappings |

The aggregate coverage figures and pure-unit figure are intentionally distinct. The
95-percent policy applies to new or materially changed production files; established
whole-repository and mutation baselines are separately ratcheted.

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
