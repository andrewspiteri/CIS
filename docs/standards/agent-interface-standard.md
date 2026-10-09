---
title: "Application CLI and Skills Standard"
type: standard
status: Active
targets:
  - backend
  - frontend
  - tooling
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: ChangeImpactStudio
  source_paths:
    - docs/specs/agent-interface-defaults-plan.md
cis:
  stable_id: change-impact-studio:standard:agent-interface
---

# Application CLI and Skills Standard

## Purpose

Make supported application workflows usable by agents through ordinary trusted application boundaries.

This starter adapts the listed ChangeImpactStudio sources. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: backend, frontend, tooling.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **INTERFACE-001** Applications SHOULD expose supported use cases through a native CLI with discoverable noninteractive commands, stable exit codes and versioned structured output; inapplicability MUST have a concrete rationale.
- **INTERFACE-002** CLI adapters MUST reuse application authorization and behavior boundaries and MUST NOT create privileged storage shortcuts.
- **INTERFACE-003** Portable skills SHOULD cover discovery, representative use, verification and diagnosis, with actual prerequisites, commands, effects and failure handling.

## Verification

- `INTERFACE-001`: Execute a representative workflow and denied/error cases without interactive prompting.
- `INTERFACE-002`: Compare CLI and other adapter outcomes and inspect trusted boundary calls.
- `INTERFACE-003`: Validate command/schema drift and reproduce the workflow from the canonical skill.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
