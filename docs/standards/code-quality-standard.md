---
title: "Code Quality and Architecture Standard"
type: standard
status: Active
targets:
  - backend
  - frontend
  - architecture
  - testing
owner: Repository maintainer
last_reviewed: 2026-11-09
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: ChangeImpactStudio
  source_paths:
    - docs/specs/code-quality-architecture-plan.md
cis:
  stable_id: change-impact-studio:standard:code-quality
---

# Code Quality and Architecture Standard

## Purpose

Keep implementation understandable, replaceable and checked with native tooling without imposing one architecture.

This starter adapts the listed ChangeImpactStudio sources. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: backend, frontend, architecture, testing.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Proportionate implementation policy

Implement the current accepted behavior with the smallest adequate set of responsibilities.

Preserve the ownership boundaries and exclusions in the accepted requirements and supplied example.
A later explicit user decision can supersede the example; an agent-generated plan or existing test
cannot independently expand the product contract.

Start with direct data structures, ordinary control flow, concrete existing code and the adopted
platform/test tools. Introduce a lifecycle, provider hierarchy, registry, persistence layer,
discovery mechanism, concurrency coordinator, public extension point or general framework only
for a specific present requirement or necessary invariant. Explain briefly why the simpler
existing approach is insufficient.

General engineering standards constrain applicable behavior; they do not authorize inventing a
capability so that the standard has something to govern.

Challenge the overall design before dividing it into smaller stories. Decomposing an unnecessary
framework into medium-sized tasks does not make it necessary.

When implementation acquires responsibilities beyond the accepted boundary, reconsider and
simplify immediately through the existing task/reflection route. Do not wait for a time limit,
repeated failure or resource exhaustion. Routine in-scope simplification should continue
autonomously; only a real unresolved product decision uses the existing human-decision route.

Tests protect behavior, invariants and real regressions. Tests of an invented mechanism do not
turn that mechanism into a requirement. Prefer independent expected values and meaningful
assertions in the native framework over another maintained behavioral verifier.

Retain the evidence needed for an actual current claim or diagnosis. Temporary workspaces,
intermediate builds and transfer bundles have an owned cleanup point; they are not permanent
evidence by default.

Apply this proportionately. Ordinary helper extraction, an existing interface used for a real
substitution, or required concurrency/security logic does not need a new ceremony. There is no
global file, class, line, test-count or code-to-test ratio limit. Human readability means a reader
can follow behavior and ownership with reasonable navigation; neither one type per operation
nor one enormous file establishes it.

## Rules

- **QUALITY-001** Changed code MUST have cohesive responsibilities, meaningful names and navigable ownership; file splitting alone MUST NOT establish readability.
- **QUALITY-002** Dependencies SHOULD be explicit and assembled at a composition boundary where replacement is needed; a DI container or interface for every class MUST NOT be required solely for conformity.
- **QUALITY-003** Applicable native analyzers, lint and formatting checks MUST execute with declared scope and enforced severity; installation alone MUST NOT count as a passing gate.
- **QUALITY-004** Architecture tests MUST enforce an owned boundary and detect a violating fixture; zero matched targets MUST NOT pass silently.
- **QUALITY-005** Independent logic review MUST use a separate identified review run, source-backed context and a selectable provider; unresolved blocking findings MUST prevent completion.
- **QUALITY-006** Implementation and review MUST preserve accepted responsibility boundaries and apply the proportionate implementation policy. Unsupported responsibilities MUST be corrected by simplifying or removing the mechanism; an agent-generated plan or passing tests MUST NOT independently expand the product contract.

## Verification

- `QUALITY-001`: Review complete types including partial declarations and representative callers.
- `QUALITY-002`: Review dependency ownership, replacement points and justified existing patterns.
- `QUALITY-003`: Run real checks and demonstrate a seeded violation; preserve stronger adopted policy.
- `QUALITY-004`: Inspect native architecture tests, selected scope and negative results.
- `QUALITY-005`: Inspect provider-neutral run/result evidence, relevant callers and finding dispositions.
- `QUALITY-006`: Inspect accepted requirements, the supplied example and superseding decisions alongside changed source and meaningful callers. Record necessity applicability and any required correction; ordinary helpers need only a concise not-applicable conclusion.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
