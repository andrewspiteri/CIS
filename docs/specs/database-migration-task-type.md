---
title: "Task Type: Database Migration"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.data.database-migration
---

# Task Type: Database Migration

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.data.database-migration` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Data and Persistence; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Data Backfill, rollout, verification, and operations |

Own deployable structural database change, compatibility sequencing, and recovery position. It changes schema objects; transformation of existing business records belongs to Data Backfill.

## Activation and inputs

### Creation evidence

- Schema, migration, table, column, index, constraint, database object, compatibility, or rollback evidence.

### Required inputs

- Approved logical model, current schema and migration history, deployment topology, compatibility window, and volume/risk evidence.

## Required activities

1. Design forward migration ordering and expand/contract compatibility when required.
2. Specify transactional behavior, locks, runtime, online/offline constraints, and deployment prerequisites.
3. Define tested rollback or explicit roll-forward recovery with a point of no return.
4. Exercise clean, upgraded, repeated, and failure/recovery paths against representative scale.
5. Update schema references and operational instructions.

## Required outputs, dependencies, and authority

- Versioned repeatable migration artifacts.
- Compatibility and rollback/roll-forward decision.
- Migration execution, timing, locking, and recovery evidence.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Clean and upgrade paths reach the expected schema repeatably without unsafe data loss.
- [ ] Application versions remain compatible for the approved deployment window.
- [ ] Recovery position, ownership, monitoring, and stop criteria are explicit and tested.

## Negative criteria

- Do not hide record transformation in schema SQL without a backfill contract.
- Do not claim rollback when destructive change makes it impossible.
- Do not run production migration as part of planning or validation.

## Validation and completion evidence

- Run migration from empty and prior supported baselines, repeat/idempotency checks, schema diff, compatibility tests, and rollback or roll-forward rehearsal.
- Record engine/version, baseline, commands, duration, locks, results, and recovery evidence.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for additive low-risk schema; medium for compatibility/locking concerns; high for destructive, large, multi-step, or cross-service change and must be decomposed.

## External issue hints

- Title: `Database Migration: <feature title>`.
- Labels: `cis`, `task-type:core.data.database-migration`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
