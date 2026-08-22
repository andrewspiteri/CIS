---
title: "Task Type: API and Consumed Contract"
type: task-type-definition
status: Draft
version: "0.2"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.api.contract
---

# Task Type: API and Consumed Contract

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.api.contract` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Documentation, Security, and Data where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Backend, frontend, integration, verification, and external consumers |

Own externally or cross-boundary consumed request, response, message, validation, error, concurrency, and compatibility semantics.

## Activation and inputs

### Creation evidence

- Endpoint, API, request, response, DTO, OpenAPI, schema, message contract, ETag, or consumer evidence.

### Required inputs

- Approved behavior, actor/security model, data semantics, existing contract versions, consumer inventory, and compatibility decisions.

## Required activities

1. Define operations, shapes, required/optional fields, validation, status/error semantics, pagination, idempotency, and concurrency where applicable.
2. Preserve or explicitly version compatibility across known consumers.
3. Implement producer and consumer boundary mappings without leaking internal models.
4. Update API/message dictionaries and generated schemas.
5. Add positive, negative, compatibility, and contract-drift tests.
6. For every unauthenticated endpoint, apply the Public Endpoint Caching Policy:
   record cache key/`Vary`, TTL/freshness, invalidation, response-cache headers,
   stampede/miss/failure behavior, and the indirect cache-population boundary.
7. Classify every endpoint as `public` (unauthenticated application data), `identity-protocol`
   (no-store credential establishment), `customer`, `backoffice`, `internal/service`, `webhook`,
   or `probe`; record its module, deployment host,
   consumers, trusted scope source, permission, abuse controls, and data-exposure boundary.
8. Apply the repository API Design and Governance specification for resource paths,
   versioning, thin transport adapters, explicit DTOs, Problem Details, pagination,
   rate limits, safe telemetry, cancellation/timeouts, files, webhooks, and security headers.
9. Keep the governed API inventory, permissions dictionary, Problem Details catalogue,
   OpenAPI document, supported-version registry, implementation, and consumer evidence
   aligned in the same change.

## Required outputs, dependencies, and authority

- Versioned API/message contract and implementation.
- Updated schemas/dictionaries and consumer-impact record.
- Compatibility, validation, error, and integration evidence.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Known consumers receive the approved semantics for valid, invalid, unauthorized, missing, conflicting, and failure cases.
- [ ] Compatibility is preserved for the approved window or a reviewed migration path exists.
- [ ] Published schema and implementation remain deterministic and drift-free.
- [ ] Every operation has an explicit exposure class, authentication/authorization
      decision, trusted scope source, named rate-limit policy, safe error contract,
      and idempotency/concurrency position where applicable.
- [ ] Resource routing, major-version lifecycle, deprecation window, and known-consumer
      compatibility follow the repository API governance specification.
- [ ] API dictionary rows, permission semantics, Problem Details identities, OpenAPI,
      implementation, and tests agree without unexplained `TBD` values.
- [ ] `PUBLIC-ENDPOINT-CACHE`: every unauthenticated operation has explicit cache
      semantics and no contract path implies a direct endpoint-to-database fallback.

## Negative criteria

- Do not expose internal persistence/domain types accidentally.
- Do not replace explicit error semantics with generic success/failure prose.
- Do not make a breaking change without a recorded compatibility decision and consumer plan.
- Do not publish an unauthenticated operation without a governed cache contract.
- Do not bind domain or persistence models directly, accept caller-selected trusted
  scope, perform business orchestration in a controller, or reveal whether a sensitive
  forbidden resource exists.
- Do not treat generated OpenAPI as a replacement for the governed row-level API inventory.

## Validation and completion evidence

- Run handler/validator, authentication, object/property authorization, abuse-control,
  schema, serialization, compatibility, consumer, idempotency/concurrency, OpenAPI-diff,
  and documentation drift checks.
- Record schema digest, operations/cases, commands, results, and consumer disposition.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one compatible operation; medium for several operations/consumers; high for versioning, public contracts, or coordinated migrations and must be decomposed.

## External issue hints

- Title: `API and Consumed Contract: <feature title>`.
- Labels: `cis`, `task-type:core.api.contract`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
