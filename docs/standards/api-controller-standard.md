---
title: "HTTP API Boundary Standard"
type: standard
status: Active
targets:
  - backend
  - api
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
    - docs/generic/standards/api-controller-standard.md
cis:
  stable_id: change-impact-studio:standard:api-controller
---

# HTTP API Boundary Standard

## Purpose

Keep HTTP endpoints thin, secure, compatible, observable, and represented by an authoritative machine-readable contract.

This starter is a classification-safe adaptation of the listed PARR standards. Repository maintainers may strengthen it, record bounded exceptions, or supersede it through reviewed canonical changes.

## Scope

Applies to: backend, api, security.

## Normative language

`MUST` and `MUST NOT` are mandatory. `SHOULD` is the expected default and requires recorded rationale when not followed. `MAY` identifies an allowed option.

## Rules

- **API-BOUNDARY-001** Every endpoint MUST declare its host or exposure class, authentication, authorization, and trusted scope source.
- **API-BOUNDARY-002** Endpoint handlers MUST remain transport adapters and MUST delegate business behavior and persistence access to application boundaries.
- **API-BOUNDARY-003** Externally reachable routes MUST use resource-oriented HTTP semantics and an explicit compatibility or versioning policy.
- **API-BOUNDARY-004** Every governed operation MUST appear in the authoritative OpenAPI document with stable operation identity, request, response, error, and security metadata.
- **API-BOUNDARY-005** Object- and property-level authorization MUST be enforced before returning or mutating protected data.
- **API-BOUNDARY-006** Errors MUST use the repository problem contract without exposing stack traces, secrets, or protected resource existence.
- **API-BOUNDARY-007** Retryable mutations MUST define idempotency and concurrency behavior.
- **API-BOUNDARY-008** Unauthenticated endpoints MUST comply with the repository public-endpoint caching and direct-database-isolation policy.

## Verification

- `API-BOUNDARY-001`: Inspect the API dictionary and generated contract.
- `API-BOUNDARY-002`: Run architecture checks or manually review endpoint dependencies.
- `API-BOUNDARY-003`: Validate the route inventory and supported-version profile.
- `API-BOUNDARY-004`: Run OpenAPI generation and documentation drift checks.
- `API-BOUNDARY-005`: Execute denied cross-object and over-posting tests.
- `API-BOUNDARY-006`: Inspect Problem Details mappings and negative-path tests.
- `API-BOUNDARY-007`: Inspect contracts and execute replay/concurrency tests.
- `API-BOUNDARY-008`: Run public endpoint architecture and cache behavior checks.

Run `cis standards validate --strict` after changing this standard or its conformance mappings.

## Exceptions

An exception requires the rule ID, human approver, rationale, bounded scope, review or expiry condition, and compensating controls. CIS and agents cannot approve exceptions.
