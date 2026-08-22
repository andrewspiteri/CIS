---
title: "Task Type: Data and Persistence"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.data.persistence
---

# Task Type: Data and Persistence

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.data.persistence` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Documentation and Security where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Migration, API, backend, lifecycle, verification, and assurance |

Own the logical data model, invariants, constraints, persistence, concurrency, audit, retention, and repository behavior. Structural migration and existing-record transformation remain separate tasks.

## Activation and inputs

### Creation evidence

- Entity, record, persistence, repository, database, audit, storage, retention, or data-model evidence.

### Required inputs

- Approved domain concepts, invariants, lifecycle, ownership, classification, retention, and access decisions.
- Current schemas/models, data dictionary, repositories, and accepted impacts.

## Required activities

1. Define entities, values, relationships, ownership, identifiers, constraints, and concurrency rules.
2. Implement persistence and transaction boundaries without leaking storage concerns into domain contracts.
3. Apply audit, retention, deletion, and sensitive-data rules.
4. Update the data dictionary and map schema/backfill needs to their dedicated tasks.
5. Test normal, invalid, concurrent, duplicate, and failure behavior.

## Required outputs, dependencies, and authority

- Approved model and persistence implementation.
- Updated data dictionary and invariant/constraint traceability.
- Repository, transaction, concurrency, audit, and retention evidence.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Stored state and invariants match the approved model under normal, invalid, and concurrent behavior.
- [ ] No excluded entity, field, duplicate source of truth, or unapproved retention is introduced.
- [ ] Focused model and persistence checks pass.

## Negative criteria

- Do not combine risky schema deployment or record backfill invisibly into this task.
- Do not persist derived or sensitive data without approved ownership and retention.
- Do not bypass domain invariants through repository shortcuts.

## Validation and completion evidence

- Run model, repository, constraint, transaction, concurrency, audit, deletion, and retention tests.
- Record model/dictionary revisions, commands, datasets, results, and residual risks.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for a local field/model adjustment; medium for several aggregates or concurrency rules; high for broad ownership, consistency, or retention changes and must be decomposed.

## External issue hints

- Title: `Data and Persistence: <feature title>`.
- Labels: `cis`, `task-type:core.data.persistence`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
