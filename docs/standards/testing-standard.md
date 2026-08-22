---
title: "Testing and Verification Standard"
type: standard
status: Active
targets:
  - testing
  - verification
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: PARR
  source_paths:
    - docs/generic/standards/testing-standard.md
    - .github/instructions/testing-pyramid.instructions.md
    - .github/instructions/tests.instructions.md
    - .github/instructions/testcontainers.instructions.md
cis:
  stable_id: change-impact-studio:standard:testing
---

# Testing and Verification Standard

## Purpose

Require a risk-proportionate layered test strategy and auditable evidence rather than treating a successful build or one test layer as proof of complete behavior.

This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: testing, verification.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Layered test model

Use distinct layers with distinct claims:

1. **Unit tests** verify isolated deterministic logic without external processes or services.
2. **Architecture and policy tests** enforce structural dependency, ownership, naming, and repository standards.
3. **Component and integration tests** verify API hosts, persistence, migrations, authentication, messaging, serialization, and real service boundaries. Testcontainers may provision a disposable dependency for this layer.
4. **Business acceptance tests** verify cross-step workflows and actor-visible outcomes in domain language.
5. **Frontend component and accessibility tests** verify local rendering, interaction, state, semantics, and emitted actions.
6. **Browser or platform journey tests** verify critical composed user workflows across deployed application boundaries.
7. **Mutation and independent assurance** challenge the strength of the preceding evidence for selected high-risk behavior.

Documentation, contract, schema, generated-artifact, and policy drift checks accompany these layers. They are deterministic verification but do not replace behavioral tests.

## Change-to-test mapping

| Change type | Minimum expected evidence |
| --- | --- |
| Domain rule, validator, mapper, or value object | Unit tests |
| Lifecycle or cross-step workflow | Unit tests plus business acceptance tests |
| Repository, migration, or real service adapter | Unit tests where useful plus component/integration tests against representative infrastructure |
| API endpoint or contract | Unit/application tests plus API integration and contract-drift tests |
| Authentication, authorization, permission, or visibility | Unit tests plus negative integration tests; architecture and journey tests where structural or user-visible |
| Event, cache, projection, retry, or replay behavior | Unit tests plus integration tests for failure, idempotency, and recovery |
| Frontend component or screen | Component/accessibility tests plus browser or platform journeys for critical composed flows |
| Architecture or standards enforcement | Architecture test, deterministic evaluator, or explicit manual conformance mapping |
| Documentation-only change | Documentation and policy compliance checks; code tests only when executable behavior changes |

## Test authoring and execution

- Prefer focused tests with one primary reason to fail, clear arrange/act/assert structure, deterministic data, and observable contract or invariant assertions.
- Treat AI-generated tests as candidates. Reject tautological tests, excessive mocks, presence-only interaction checks, and snapshot updates with no explained behavioral change.
- Run the smallest meaningful affected layer during iteration, then broaden according to impact and release risk.
- Pin container images or modules, wait for service readiness rather than fixed sleeps, isolate state, dispose resources, and never silently replace a required real dependency with a fake.
- Keep test framework and folder conventions repository-specific. The layered strategy is portable; PARR-specific xUnit, ReqNroll, Stryker.NET, Playwright for .NET, and PostgreSQL choices are examples rather than universal CIS defaults.

## Rules

- **TEST-001** Every change MUST map its acceptance criteria, affected boundaries, and material risks to the applicable test layers before completion is claimed.
- **TEST-002** Changed deterministic domain, application, validation, mapping, or policy behavior MUST have focused unit tests covering expected results, boundaries, invalid inputs, and material denied paths.
- **TEST-003** Tests classified as unit tests MUST remain fast and deterministic and MUST NOT require network access, external processes, containers, or shared mutable services.
- **TEST-004** API, persistence, migration, authentication, messaging, serialization, and other integration boundaries MUST have component or integration tests against representative infrastructure whenever substitutes cannot reproduce the relevant semantics.
- **TEST-005** Testcontainers or an equivalent disposable harness SHOULD provision real containerized dependencies when their protocol or runtime semantics materially affect correctness; it is a provisioning mechanism, not a separate test layer.
- **TEST-006** Cross-step workflows, lifecycle transitions, and actor-visible business outcomes MUST have acceptance-style tests when focused unit and integration cases do not prove the complete outcome.
- **TEST-007** Architecture rules claimed as enforceable MUST have a deterministic architecture test or an explicit conformance mapping naming the manual evidence owner.
- **TEST-008** Frontend changes MUST use component or interaction tests for local behavior and browser or platform journey tests for critical composed workflows; one layer MUST NOT be reported as the other.
- **TEST-009** Bug fixes MUST include a regression test that fails for the reproduced defect when technically feasible.
- **TEST-010** High-value domain, authorization, lifecycle, calculation, migration, or historically fragile logic SHOULD receive mutation testing or another recorded independent assurance technique beyond line coverage.
- **TEST-011** Testable new or materially changed production behavior SHOULD maintain at least 95 percent line coverage unless the repository defines a stronger threshold or records a bounded human-approved exception; coverage MUST NOT replace behavior assertions.
- **TEST-012** CI and completion evidence MUST distinguish every applicable layer that passed, failed, was skipped, or could not run, including Docker, browser, credential, and environment limitations.

## Verification

- `TEST-001`: Inspect the feature or task verification matrix and trace each selected layer to acceptance or risk.
- `TEST-002`: Execute the narrow unit suite and review assertions for observable behavior rather than implementation mirroring.
- `TEST-003`: Inspect unit fixtures and execute the suite without external service or container prerequisites.
- `TEST-004`: Execute the targeted component or integration suite and review its real-boundary evidence.
- `TEST-005`: Review pinned dependency versions, readiness, isolation, cleanup, runtime prerequisites, and actual container-backed execution evidence.
- `TEST-006`: Execute the focused business or acceptance scenarios and confirm they use domain language and thin fixtures.
- `TEST-007`: Run structural checks or inspect the conformance matrix and recorded manual evidence.
- `TEST-008`: Execute the applicable component and journey suites and inspect accessible selectors, state coverage, and failure artifacts.
- `TEST-009`: Review the preserved reproduction and confirm the new or changed test distinguishes broken from corrected behavior.
- `TEST-010`: Review bounded mutation, security, architecture, property, or independent-review evidence and disposition surviving risks.
- `TEST-011`: Review changed-scope coverage, exclusions, meaningful assertions, and any approved exception.
- `TEST-012`: Inspect exact commands, results, artifacts, unrun checks, environmental limits, and residual risks.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
