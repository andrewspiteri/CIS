---
title: "Task Type: Consumed Contract"
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

# Task Type: Consumed Contract

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.api.contract` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Documentation, Security, and Data where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Backend, frontend, integration, verification, and external consumers |

Own externally or cross-boundary consumed HTTP, message, CLI, process, JSON, event,
validation, error, cancellation, concurrency, and compatibility semantics.

## Activation and inputs

### Creation evidence

- Endpoint, API, request, response, DTO, OpenAPI, schema, message, command arguments,
  exit codes, structured output, JSONL events, process lifecycle, or consumer evidence.

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
   aligned in the same change when an HTTP API is affected.
10. For CLI or process contracts, define executable discovery, argument arrays, exit codes,
    stdout/stderr ownership, JSON schema/version, event streaming, cancellation, timeout,
    malformed/partial/oversized output, and compatibility behavior without shell parsing.

## Required outputs, dependencies, and authority

- Versioned HTTP/message/CLI/process contract and implementation.
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
- [ ] When HTTP APIs are affected, API dictionary rows, permission semantics, Problem
      Details identities, OpenAPI, implementation, and tests agree without unexplained
      `TBD` values.
- [ ] When CLI or process contracts are affected, arguments, exit codes, output schema,
      stream ownership, cancellation, timeout, malformed-output handling, implementation,
      and consumer tests agree.
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
- Do not reinterpret CLI output presentation text, build a shell command string, or infer
  success from process exit alone when a structured result is required.

## Validation and completion evidence

- Run applicable adapter/handler, authentication, authorization, abuse-control, schema,
  serialization, exit-code, malformed-output, cancellation/timeout, compatibility,
  consumer, idempotency/concurrency, and documentation-drift checks. Run OpenAPI diff
  only for affected HTTP APIs.
- Record schema digest, operations/cases, commands, results, and consumer disposition.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one compatible operation; medium for several operations/consumers; high for versioning, public contracts, or coordinated migrations and must be decomposed.

## External issue hints

- Title: `Consumed Contract: <feature title>`.
- Labels: `cis`, `task-type:core.api.contract`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
