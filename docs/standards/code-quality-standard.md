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
last_reviewed: 2026-08-15
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

## Rules

- **QUALITY-001** Changed code MUST have cohesive responsibilities, meaningful names and navigable ownership; file splitting alone MUST NOT establish readability.
- **QUALITY-002** Dependencies SHOULD be explicit and assembled at a composition boundary where replacement is needed; a DI container or interface for every class MUST NOT be required solely for conformity.
- **QUALITY-003** Applicable native analyzers, lint and formatting checks MUST execute with declared scope and enforced severity; installation alone MUST NOT count as a passing gate.
- **QUALITY-004** Architecture tests MUST enforce an owned boundary and detect a violating fixture; zero matched targets MUST NOT pass silently.
- **QUALITY-005** Independent logic review MUST use a separate identified review run, source-backed context and a selectable provider; unresolved blocking findings MUST prevent completion.

## Verification

- `QUALITY-001`: Review complete types including partial declarations and representative callers.
- `QUALITY-002`: Review dependency ownership, replacement points and justified existing patterns.
- `QUALITY-003`: Run real checks and demonstrate a seeded violation; preserve stronger adopted policy.
- `QUALITY-004`: Inspect native architecture tests, selected scope and negative results.
- `QUALITY-005`: Inspect provider-neutral run/result evidence, relevant callers and finding dispositions.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
