---
title: "Task Type: Backend Behavior"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.backend.behavior
---

# Task Type: Backend Behavior

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.backend.behavior` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | API, Data, and Security where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Frontend, integration, observability, lifecycle, verification, and assurance |

Own domain and application behavior, workflows, commands, rules, invariants, state transitions, orchestration, and concurrency behind approved boundaries.

## Activation and inputs

### Creation evidence

- Backend, domain, service, handler, command, workflow, business rule, invariant, or state-transition evidence.

### Required inputs

- Approved requirements, decisions, contracts, data model, permissions, workflow/state dictionaries, and accepted impacts.

## Required activities

1. Implement positive and prohibited behavior in the correct domain/application boundary.
2. Preserve invariants, authorization context, idempotency, transaction, concurrency, and failure semantics.
3. Avoid parallel workflows or bypass paths around canonical ownership.
4. Update commands, events, workflows, invariants, errors, and ownership references.
5. Add focused unit, application, integration, concurrency, and regression tests.
6. For unauthenticated endpoints, route every response through a governed cache
   abstraction. Keep database contexts/clients, repositories, and persistence query
   providers out of the route/controller/handler, including its cache-miss path.

## Required outputs, dependencies, and authority

- Domain/application implementation and updated behavior references.
- Positive, negative, transition, concurrency, and failure evidence.
- Known limitations, deferrals, and residual-risk record.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Approved workflows and invariants hold across success, invalid, conflict, retry, concurrent, and failure cases.
- [ ] No alternate source of truth, unauthorized bypass, or unapproved side effect is introduced.
- [ ] Focused and affected deterministic tests pass.
- [ ] `PUBLIC-ENDPOINT-CACHE`: cache population is delegated behind an
      application/query boundary and architecture plus integration tests prove the
      public endpoint neither depends directly on persistence nor bypasses the cache.

## Negative criteria

- Do not move product decisions into hidden implementation defaults.
- Do not bypass contracts, permissions, or persistence invariants for convenience.
- Do not treat mocks alone as proof of cross-boundary behavior.
- Do not query a database or repository directly from an unauthenticated endpoint
  boundary, even as a cache-miss fallback.

## Validation and completion evidence

- Run domain, application, handler, integration, concurrency, idempotency, failure, and regression suites.
- Record commands, cases, results, state/event evidence, and residual risks.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one bounded rule; medium for a multi-step workflow; high for cross-aggregate, concurrent, or uncertain domain change and must be decomposed.

## External issue hints

- Title: `Backend Behavior: <feature title>`.
- Labels: `cis`, `task-type:core.backend.behavior`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
