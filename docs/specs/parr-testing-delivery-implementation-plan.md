---
title: "PARR Testing and Delivery Assurance Implementation Plan"
type: implementation-plan
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on milestone completion or gap-matrix change"
cis:
  stable_id: change-impact-studio:plan:parr-testing-delivery-assurance
---

# PARR testing and delivery assurance implementation plan

## 1. Goal

Close the open rows in the
[PARR testing and delivery gap matrix](parr-testing-delivery-gap-matrix.md) by making
test-layer selection, execution, traceability, coverage, regression, mutation,
independent assurance, and CI enforcement deterministic CIS capabilities. Prove the
capabilities by migrating the Friends Todo reference workspace without changing its
approved product behavior.

## 2. Planning principles

1. Markdown remains canonical; inventories, parsed result files, and run state live under
   `.cis/local/`.
2. CIS discovers, classifies, validates, and orchestrates repository-owned test tools. It
   does not replace Vitest, Playwright, Stryker, `dotnet test`, or other native runners.
3. Testcontainers is an integration provisioning mechanism, never a test layer.
4. A unit-test claim requires isolation from listeners, filesystems used as production
   persistence, containers, external processes, networks, and shared mutable services.
5. Test applicability is derived from repository classification, impact, requirement
   type, risk, and approved plan. An agent cannot silently mark an applicable layer as
   not applicable.
6. Mechanical test layers do not add human approval points. One approved delivery plan,
   explicit approval of bounded exceptions or residual risk, and final human acceptance
   remain sufficient.
7. Existing test behavior is preserved during folder and command migration. A move is
   not an opportunity to weaken assertions or rewrite product behavior.
8. A source reference proves discoverability, not execution. Completion evidence must
   bind test identity to an exact run result.
9. PARR's .NET-specific frameworks are not CIS defaults. Target repositories retain
   their native stack while adopting the portable layer and assurance contract.

## 3. Target model

### 3.1 Canonical test-suite profile

Initialization seeds `references/test-suite-profile.md`. Each registered suite records:

- stable suite ID and repository ID;
- `unit`, `architecture`, `component`, `integration`, `business`,
  `frontend-component`, `browser`, `security`, or `mutation` layer;
- native framework and exact argument-list command;
- owned production paths and excluded generated/vendor paths;
- external process, network, filesystem, container, browser, credential, and secret
  requirements;
- supported operating systems and execution environment;
- result and coverage formats;
- timeout, isolation, cleanup, and artifact rules;
- feature, risk, or change signals that make the suite applicable; and
- CI, release, optional provider-smoke, or local-only enforcement tier.

The profile is canonical because maintainers may refine commands and applicability.
Discovery proposes additions or drift; it does not overwrite reviewed repository policy.

### 3.2 Derived inventory and run evidence

CIS stores disposable state under:

```text
.cis/local/testing/inventory.json
.cis/local/testing/runs/<run-id>/manifest.json
.cis/local/testing/runs/<run-id>/results.json
.cis/local/testing/runs/<run-id>/artifacts.json
```

The run manifest binds repository revisions, suite profile digests, exact commands,
environment, started/finished timestamps, discovered test IDs, results, coverage,
mutation scores, artifacts, and infrastructure failures. Canonical verification records
summarize and hash-bind the selected run evidence without committing raw coverage,
browser videos, containers, or large logs.

### 3.3 CLI boundary

Add a `test` module with bounded commands:

```text
cis test inventory
cis test validate [--strict]
cis test trace --change <id>
cis test status --change <id>
```

Execution remains owned by shell-free CIS workflows. `cis workflow run` consumes the
registered argument-list commands, and the test module parses and reconciles results.
No command treats a zero exit code without expected result output as proof that tests
ran.

## 4. Workstreams and sequence

### Milestone 0 - Baseline and contract tests

**Closes or prepares:** GAP-TEST-001, GAP-TEST-014.

1. Capture golden fixtures for the current Friends Todo API, web, infrastructure, and
   authority repositories.
2. Define the suite-profile schema, layer taxonomy, applicability rules, result model,
   run identity, and canonical/derived boundary.
3. Add fixtures proving that HTTP-listener and real-SQLite tests cannot be reported as
   pure unit suites merely because their folder is named `unit`.
4. Add validation cases for missing commands, duplicate suite IDs, unsupported result
   formats, vendor/generated paths, absent result files, skipped tests, and stale source
   references.

**Exit:** the contract rejects the current Friends Todo misclassification before any
repository files are reorganized.

### Milestone 1 - Classification-driven initialization and workflow repair

**Closes or prepares:** GAP-PROC-007, GAP-TEST-001, GAP-TEST-018.

1. Detect native test scripts and frameworks from `package.json`, project files,
   Playwright/Vitest configuration, Compose, Terraform, and known result files.
2. Seed a repository-specific suite profile and `standard-delivery.md`; never seed
   .NET commands into a Node, frontend, documentation, or infrastructure repository
   without supporting classification evidence.
3. Reconcile profiles idempotently as repositories or suites are added later.
4. Extend Repository Doctor to report classification/command disagreement and offer a
   bounded profile correction.
5. Update classification-selected testing skills and instructions to require inventory,
   validation, applicable execution, and `repo doctor` after a failed reconciliation.

**Exit:** reinitializing the four Friends Todo repositories produces correct native
workflows with no erroneous `dotnet` command and no human-managed file loss.

### Milestone 2 - Planning and verification enforcement

**Closes or prepares:** GAP-TEST-014, GAP-TEST-017, GAP-PROC-009.

1. Extend feature planning with a structured test-obligation matrix derived from
   requirement type, repository boundary, risk, and frontend classification.
2. Keep the existing Verification and Independent Assurance task types. Represent test
   layers as obligations inside those tasks rather than creating approval-heavy task
   types for every suite.
3. Require an explicit evidence-backed disposition for every applicable layer. A human
   must approve exceptions to mandatory standards; ordinary successful execution needs
   no new approval.
4. Make `cis verify validate` reconcile stable `TC-*` IDs against discovered and passed
   test results from a bound run, not source text alone.
5. Classify tool failures as product failure, assertion failure, runner/infrastructure
   failure, missing prerequisite, cancellation, or unknown. Preserve the first failure
   and a bounded clean rerun without erasing history.
6. Record implementer and assurer identities and reject a required independent-assurance
   claim that has no distinct reviewer or approved independent mechanical technique.

**Exit:** a source-only `TC-*` match cannot satisfy completion, an unrun applicable
layer blocks verification, and no extra human approval is requested for passing tests.

### Milestone 3 - Native result adapters and CI templates

**Closes or prepares:** GAP-PROC-008, GAP-PROC-010, GAP-TEST-011, GAP-TEST-018.

1. Parse JUnit-compatible results plus native Vitest/V8 coverage, Playwright, Stryker,
   and .NET test/coverage formats needed by CIS and its samples.
2. Provide classification-selected CI templates for documentation authority, Node API,
   Next.js frontend, infrastructure, and .NET repositories.
3. Use self-hosted build-server labels only through repository policy; keep credentials
   and SSH details outside committed workflow definitions.
4. Run affected pull-request layers first and the full applicable chain before release.
5. Upload bounded logs, coverage summaries, mutation reports, and browser failure
   artifacts. Do not commit large derived output.
6. Prevent artifact promotion when a required validation, freshness check, mutation
   threshold, or browser regression gate fails.

**Exit:** a representative pull request cannot merge with a missing required suite, and
a release run cannot publish artifacts after a failed assurance gate.

### Milestone 4 - Friends Todo suite separation

**Closes:** GAP-TEST-002, GAP-TEST-003, GAP-TEST-004, GAP-TEST-005, GAP-TEST-007.

1. Split `todo-api` into independently runnable unit, component/API, SQLite integration,
   external integration, architecture, business, security, and mutation scopes.
2. Move HTTP-listener tests to component/API scope and real SQLite migration,
   transaction, backup, and restore tests to SQLite integration scope without weakening
   their assertions.
3. Retain isolated lifecycle, validation, contract, mapping, cache, and policy behavior
   as pure unit tests.
4. Split `todo-web` into pure utility/contract tests and frontend component/accessibility
   tests while retaining Testing Library interaction behavior.
5. Register infrastructure topology, runtime smoke, recovery drill, and operations checks
   as executable integration/operational suites rather than untyped scripts.
6. Add business-readable acceptance suites for:
   - cloud or anonymous actor establishment and session boundary;
   - owner list and todo lifecycle with optimistic concurrency;
   - common-link request, owner approval/rejection, membership, and removal;
   - owner/member capability separation and non-disclosure; and
   - specific-session and actor-wide termination.

**Exit:** CIS reports independent counts and results for every applicable layer and can
state pure unit coverage without including listeners, SQLite, containers, or browsers.

### Milestone 5 - Coverage, mutation, and architecture assurance

**Closes:** GAP-TEST-006, GAP-TEST-010, GAP-TEST-011, GAP-TEST-012.

1. Implement changed-production-code coverage with the CIS/PARR 95-percent expectation,
   explicit narrow exclusions, and human-approved bounded exceptions.
2. Introduce a JavaScript mutation runner for high-value Friends Todo lifecycle,
   authorization, ownership, concurrency, token, and revocation logic.
3. Start feature/PR mutation in report-only mode with a recorded baseline and survivor
   dispositions. Ratchet touched scopes without lowering an existing score.
4. Use the PARR release baseline of advisory `high: 85`, `low: 80`, and enforcing
   `break: 80` unless a reviewed CIS decision selects another repository-specific policy.
5. Add deterministic TypeScript dependency/architecture rules for transport,
   application, persistence, public-cache, frontend classification, and security
   boundaries.
6. Bind reproduced golden-path defects to named regression tests and executed-run
   evidence.

**Exit:** changed-code coverage, architecture rules, regression identities, and the
selected mutation tier are machine-readable blocking evidence.

### Milestone 6 - Browser, security, and deployment-mode regression

**Closes:** GAP-TEST-008, GAP-TEST-009, GAP-TEST-015, GAP-TEST-016,
GAP-TEST-019.

1. Break the single long Playwright journey into feature-scoped page-object scenarios
   for authentication/session, list/todo, sharing/membership, unavailable/recovery, and
   session termination behavior.
2. Preserve at least one thin full-product golden journey without making it the only
   regression signal.
3. Configure trace retention, failure screenshots, optional failure video, console and
   request diagnostics, deterministic test data, and bounded artifact upload.
4. Keep canonical retries at zero. A diagnostic rerun is recorded separately and cannot
   hide a flaky first result.
5. Add classified dependency, secret, static-analysis, and optional dynamic-security
   profiles. Negative-principal and non-disclosure tests remain behavioral gates.
6. Define an opt-in managed-cloud/Google smoke profile that is never required for local
   anonymous delivery and never places provider credentials in canonical Markdown.

**Exit:** each critical composed workflow has an isolated regression, failures preserve
useful artifacts, and unavailable optional provider credentials are reported accurately
rather than silently skipped.

### Milestone 7 - Historical traceability backfill

**Closes:** GAP-TEST-013, GAP-TEST-014.

1. Generate Markdown and CSV test cases for CIS-0001 through CIS-0006 from their approved
   feature specifications without changing the approved requirements.
2. Reconcile CIS-0007 through CIS-0010 mappings against executed test identities.
3. Move generated catalogue lifecycle from Draft only when the source digest, CSV hash,
   live automation references, and bound execution evidence all validate.
4. Record justified historical limitations rather than inventing tests or execution
   dates.

**Exit:** every Friends Todo feature has a current human-readable/CSV catalogue and every
Automated label is backed by a passed test in a recorded run.

### Milestone 8 - Independent assurance and final hardening

**Closes:** GAP-PROC-006, GAP-PROC-008, GAP-PROC-009, GAP-PROC-010,
GAP-TEST-017, GAP-TEST-018, GAP-TEST-019.

1. Seed the missing feature-specification governance skill and link it from initialized
   agent instructions.
2. Add independent-assurance assignment and technique evidence without requiring a
   separate human team for every low-risk change.
3. Run CIS focused tests, full solution tests, strict documentation/skills/standards
   validation, package validation, and the complete Friends Todo golden path.
4. Exercise failure cases: test assertion, absent Docker, browser crash, resource
   exhaustion, missing provider credential, stale test mapping, coverage regression,
   surviving mutant, and release artifact block.
5. Re-score every gap-matrix row and record remaining exceptions or residual risks for
   human review.

**Exit:** all P0 and P1 rows are Closed, every remaining P2 row has either closure or an
explicit bounded human-approved disposition, and release evidence is reproducible from
repository commands and CI.

## 5. Priority slices

| Slice | Scope | Why first |
| --- | --- | --- |
| P0-A | Suite-profile model, inventory, validation, and classification tests | Every later metric depends on truthful layer identity. |
| P0-B | Repository-specific workflow generation and Repository Doctor checks | Prevents CIS from prescribing invalid commands before another repository is initialized. |
| P0-C | Executed-test result binding and verification enforcement | Stops source references from being mistaken for passing tests. |
| P0-D | Baseline PR CI for CIS and Friends Todo repositories | Converts manually repeated evidence into continuous protection. |
| P1-A | Friends Todo unit/component/integration/business separation | Produces honest suite counts and enables layer-specific failure diagnosis. |
| P1-B | Changed-code coverage, mutation baseline, and architecture enforcement | Tests test quality and the highest-risk boundaries rather than relying on line coverage. |
| P1-C | Feature-scoped browser regression and artifacts | Protects user-visible composed behavior and makes failures diagnosable. |
| P1-D | Release ordering, artifact gate, and infrastructure-failure classification | Brings delivery enforcement to PARR-equivalent maturity. |
| P2 | Historical case backfill, provider smoke, expanded security, and stronger reviewer separation | Completes traceability and depth after the core enforcement path is trustworthy. |

## 6. CIS implementation verification

Every milestone must include:

- unit tests for schema, classification, validation, and result parsing;
- integration fixtures using temporary .NET, Node API, frontend, infrastructure, and
  multi-repository workspaces;
- idempotent initialization and migration tests preserving human-managed profiles;
- command tests for human, JSON, and agent output plus stable exit codes;
- negative tests for absent, stale, skipped, malformed, partial, and contradictory
  evidence;
- strict documentation, skills, standards, graph, package, and manual validation; and
- a clean run on the configured Linux build server using the `/data` workspace/toolchain.

## 7. Completion contract

This plan is complete only when:

1. the gap matrix is re-evaluated from current repository evidence;
2. all P0 and P1 gaps meet their target maturity;
3. Friends Todo reports separate unit, component, integration, business, architecture,
   frontend, browser, security, and mutation outcomes;
4. pure unit and changed-code coverage are reported without layer contamination;
5. required test identities are proven executed and passed;
6. mutation survivors and score movement have durable dispositions;
7. pull-request and release gates are repository-owned and reproducible;
8. browser and infrastructure failures retain bounded diagnostics;
9. no product behavior or approval is silently changed during test migration; and
10. final human acceptance is requested once for the completed assurance-hardening
    outcome, not once per mechanical test layer.

