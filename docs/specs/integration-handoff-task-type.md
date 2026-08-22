---
title: "Task Type: Integration and Handoff"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.integration.handoff
---

# Task Type: Integration and Handoff

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.integration.handoff` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Backend, Frontend, and API where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Infrastructure, observability, verification, rollout, and assurance |

Own authoritative cross-module, cross-repository, event, queue, external-system, and human/system handoffs without creating parallel subsystems.

## Activation and inputs

### Creation evidence

- Integration, handoff, external system, publish/subscribe, event, linked task, queue, callback, webhook, or cross-module evidence.

### Required inputs

- Producer/consumer contracts, ownership map, permissions, workflow states, correlation/provenance rules, retry/idempotency decisions, and failure expectations.

## Required activities

1. Define ownership and state at every boundary before and after handoff.
2. Implement contract mapping, correlation, provenance, authorization, idempotency, timeout, retry, ordering, and failure recovery.
3. Make partial success, duplicate delivery, late delivery, and unavailable dependency behavior explicit.
4. Update event/command/workflow and ownership references.
5. Test with realistic boundary substitutes or controlled integration environments.

## Required outputs, dependencies, and authority

- Authoritative integration implementation and updated contracts/maps.
- Handoff state/provenance and failure-recovery evidence.
- Operational dependency and residual-risk record.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Handoffs preserve identity, authorization, provenance, canonical state, and approved failure semantics.
- [ ] Retries, duplicates, timeouts, partial failure, and recovery do not create inconsistent or parallel truth.
- [ ] Contract and end-to-end integration checks pass.

## Negative criteria

- Do not use shared database access as an undocumented integration contract.
- Do not acknowledge work before authoritative durability unless explicitly designed.
- Do not let external availability turn deterministic failures into silent success.

## Validation and completion evidence

- Run producer/consumer contract, integration, duplicate, ordering, timeout, retry, partial-failure, and recovery tests.
- Record environments/substitutes, correlation evidence, commands, results, and unresolved dependency risk.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one stable boundary; medium for asynchronous/failure handling; high for multi-system, cross-repository, or distributed consistency and must be decomposed.

## External issue hints

- Title: `Integration and Handoff: <feature title>`.
- Labels: `cis`, `task-type:core.integration.handoff`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
