---
title: "Secure Feature Implementation Standard"
type: standard
status: Active
targets:
  - security
  - backend
  - frontend
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: PARR
  source_paths:
    - docs/generic/standards/secure-feature-implementation-standard.md
cis:
  stable_id: change-impact-studio:standard:secure-feature-implementation
---

# Secure Feature Implementation Standard

## Purpose

Make authorization, data exposure, secrets, validation, and security verification explicit parts of application delivery.

This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: security, backend, frontend.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **SEC-FEAT-001** Every externally reachable operation MUST declare its authentication and authorization expectations.
- **SEC-FEAT-002** Authorization MUST be enforced at the trusted server or platform boundary and MUST NOT rely only on hidden or disabled user-interface controls.
- **SEC-FEAT-003** Caller-controlled identifiers MUST NOT expand tenant, customer, object, or privilege scope beyond the validated security context.
- **SEC-FEAT-004** Sensitive values MUST NOT be written to source, generated documentation, logs, model prompts, or user-visible errors.
- **SEC-FEAT-005** Untrusted input MUST be bounded and validated before it reaches domain, persistence, command, rendering, or external-service boundaries.
- **SEC-FEAT-006** Security-significant changes MUST include explicit abuse cases and verification evidence.

## Verification

- `SEC-FEAT-001`: Inspect contract and permission references.
- `SEC-FEAT-002`: Test denied access at the trusted enforcement boundary.
- `SEC-FEAT-003`: Test cross-scope and object-level access attempts.
- `SEC-FEAT-004`: Run secret scanning and inspect logging/error paths.
- `SEC-FEAT-005`: Test invalid, oversized, and malicious input cases.
- `SEC-FEAT-006`: Review security tasks, tests, and independent assurance.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
