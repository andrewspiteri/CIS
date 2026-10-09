---
title: "Edge Security Header Standard"
type: standard
status: Active
targets:
  - frontend
  - backend
  - infrastructure
  - security
stacks:
  - http
owner: Repository maintainer
last_reviewed: 2026-08-15
review_cadence: on change
source_of_truth: This file
provenance:
  method: curated adaptation
  source_repository: PARR
  source_paths:
    - docs/generic/standards/edge-security-header-standard.md
cis:
  stable_id: change-impact-studio:standard:edge-security-header
---

# Edge Security Header Standard

## Purpose

Assign one clear owner for browser security headers, caching directives, and environment-specific edge exceptions.

This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: frontend, backend, infrastructure, security.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **EDGE-HDR-001** Each security or caching header MUST have one authoritative configuration owner for a deployed route.
- **EDGE-HDR-002** Conflicting duplicate security headers MUST be rejected by deterministic deployment or integration validation.
- **EDGE-HDR-003** HSTS MUST use a staged rollout and MUST NOT be enabled for an environment before HTTPS behavior is verified.
- **EDGE-HDR-004** Cache-control behavior MUST preserve the repository policy for authenticated, sensitive, and unauthenticated responses.
- **EDGE-HDR-005** Environment exceptions MUST identify an owner, rationale, bounded scope, and removal or review condition.

## Verification

- `EDGE-HDR-001`: Inspect edge, host, and application configuration for duplicate ownership.
- `EDGE-HDR-002`: Run deployed-header validation against representative routes.
- `EDGE-HDR-003`: Review environment rollout evidence and HTTPS probes.
- `EDGE-HDR-004`: Execute response-header and cache-isolation tests.
- `EDGE-HDR-005`: Review exception records and deployed configuration.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
