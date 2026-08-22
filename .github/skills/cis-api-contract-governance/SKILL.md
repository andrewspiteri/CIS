---
name: cis-api-contract-governance
description: Design, change, and verify HTTP APIs against repository exposure, security, compatibility, inventory, OpenAPI, and drift rules.
---

# CIS API Contract Governance

## Purpose

Apply the repository API standard as an executable delivery checklist while preserving the governed contract inventories.

## When to Use

Use for every new or changed endpoint, route, request/response contract, permission, error, version, webhook, file transfer, or externally consumed behavior.

## Workflow

1. Read `docs/specs/api-design-and-governance-spec.md`, the Public Endpoint Caching Policy, and affected contract references.
2. Classify the operation as `public`, `customer`, `backoffice`, `internal/service`, `webhook`, or `probe`; identify module, host/deployment, consumers, trusted scope, permission, and data boundary.
3. Prefer a compatible resource extension before adding a near-duplicate route. Record resource path, major version, lifecycle, and compatibility decision.
4. Keep the controller/handler thin and use explicit DTOs. Define validation, statuses, stable Problem Details, pagination, rate limits, idempotency, concurrency, cancellation, timeouts, and safe telemetry as applicable.
5. For `public` endpoints, enforce `PUBLIC-ENDPOINT-CACHE`: every response traverses a governed cache and the endpoint never directly accesses a database or repository, including on cache miss.
6. Co-update the API dictionary, permissions dictionary, Problem Details catalogue, supported-version record, OpenAPI, implementation, and tests wherever the change affects them.
7. Export OpenAPI deterministically and compare it with every supported baseline in the same major-version line using forward-transitive compatibility, then run authorization/abuse, integration, schema-diff, and documentation drift checks.

## Guardrails

Do not accept caller-selected trusted scope, bind domain/persistence models, bury business logic in controllers, expose sensitive resource existence, treat OpenAPI as the only inventory, or leave derivable governance fields as `TBD`.

## Output Expectations

Produce aligned implementation and contract artifacts with stable identities, explicit lifecycle/consumer disposition, commands, results, and evidence paths or digests.
